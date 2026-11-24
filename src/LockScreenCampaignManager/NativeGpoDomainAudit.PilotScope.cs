using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// AD-only, per-object scope projection for the existing all-DC GPO scanner.
/// Does NOT use gpresult XML or start any endpoint process. Evaluates exact
/// computer/user LDAP objects, tokenGroups, ancestor OU links, block/enforce,
/// policy half flags, GPO DACL Read + Apply and possible loopback. Site/WMI,
/// GPP item targeting, local/MDM policy and client application stay UNKNOWN.
/// </summary>
internal static partial class NativeGpoDomainAudit
{
    private static readonly Guid ScopeApplyRight =
        new("edacfd8f-ffb3-11d1-b41d-00a0c968f939");
    private sealed record ScopeSubject(string Dn, string Name, string Sid,
        string TokenStatus, IReadOnlySet<string> TokenSids);
    private sealed record ScopeLevel(string Dn, int Depth, bool Block,
        IReadOnlyList<NativeGpoLifecycleList.ParsedLink> Links);
    private sealed record ScopedLink(
        string Gpo, string GpoId, string Scope, string Origin,
        string LinkedAt, int Depth, int Order, bool Enforced,
        string State, string Security, string Wmi, string LoopbackValue,
        string ComputerRead);
    private sealed record ScopedRows(
        IReadOnlyList<ScopedLink> Rows, bool Complete);

    private static JsonElement ExecutePilotScope(object request,
        DomainCredentials credentials)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Signed Windows LDAP required.");
        var args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Required(args, "domain", 253).ToLowerInvariant();
        string dc = Required(args, "dc", 253);
        string computer = Required(args, "computer", 253).ToLowerInvariant();
        string computerOu = Required(args, "computerOu", 2048);
        string userName = args.TryGetProperty("user", out var rawUser) &&
            rawUser.ValueKind == JsonValueKind.String
                ? rawUser.GetString()?.Trim() ?? "" : "";
        NativeAdDiscovery.ValidateNames(domain, dc);
        if (!NativeGpoConflictScan.SafeComputerName(computer, domain) ||
            !computerOu.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
            computerOu.Any(char.IsControl) ||
            userName.Length > 256 || userName.Any(char.IsControl) ||
            userName.Contains('\\') || userName.Contains('/') ||
            userName.Contains('*') || userName.Contains('(') ||
            userName.Contains(')') ||
            userName.StartsWith("@", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid domain pilot identity or selected Computer OU.");

        JsonElement Run()
        {
            using var ldap = NativeAdDiscovery.Connect(dc, credentials);
            ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            var root = NativeAdDiscovery.ReadRootDse(ldap);
            string baseDn = NativeAdDiscovery.Attr(root, "defaultNamingContext");
            string actualDc = NativeAdDiscovery.Attr(root, "dnsHostName");
            if (!NativeAdDiscovery.DomainFromDn(baseDn).Equals(domain,
                    StringComparison.OrdinalIgnoreCase) ||
                !NativeGpoLifecycleList.SameController(actualDc, dc) ||
                !NativeGpoConflictScan.Inside(computerOu, baseDn))
                throw new InvalidDataException("Pilot DC/domain/OU mismatch.");
            _ = ReadOne(ldap, computerOu, "(objectClass=organizationalUnit)",
                "distinguishedName");
            ScopeSubject machine = ResolveComputer(ldap, computerOu, computer);
            string machineParent = NativeGpoConflictScan.ParentDn(machine.Dn) ??
                throw new InvalidDataException("Missing computer parent.");
            var machineLevels = ReadAncestry(ldap, machineParent, baseDn);
            var machineGpos = ReadScopedLinks(ldap, machineLevels, machine,
                actualDc, domain, baseDn, "Computer", "Computer OU");
            string loopback = DecideLoopback(machineGpos.Rows, machineGpos.Complete);

            ScopeSubject? user = null;
            string actualUserOu = "";
            ScopedRows normalUserGpos = new([], true);
            ScopedRows loopbackUserGpos = new([], true);
            if (userName.Length > 0)
            {
                user = ResolveUser(ldap, baseDn, userName);
                actualUserOu = NativeGpoConflictScan.ParentDn(user.Dn) ??
                    throw new InvalidDataException("Missing user parent.");
                var userLevels = ReadAncestry(ldap, actualUserOu, baseDn);
                normalUserGpos = ReadScopedLinks(ldap, userLevels, user,
                    actualDc, domain, baseDn, "User", "User OU", machine);
                if (loopback != "Not configured")
                    loopbackUserGpos = ReadScopedLinks(ldap, machineLevels, user,
                        actualDc, domain, baseDn, "User", "Computer OU (loopback)",
                        machine);
                // Replace may skip User OU GPOs, but the endpoint's actual
                // policy processing remains unverified by this DC scan.
                normalUserGpos = normalUserGpos with {
                    Rows = normalUserGpos.Rows.Select(x => x with {
                        State = QualifyLoopbackUserRow(x.State, loopback, false)
                    }).ToArray()
                };
                loopbackUserGpos = loopbackUserGpos with {
                    Rows = loopbackUserGpos.Rows.Select(x => x with {
                        State = QualifyLoopbackUserRow(x.State, loopback, true)
                    }).ToArray()
                };
            }
            var rows = machineGpos.Rows.Concat(normalUserGpos.Rows)
                .Concat(loopbackUserGpos.Rows).ToArray();
            if (rows.Length > 750)
                throw new InvalidDataException("Bounded GPO scope projection exceeded 750 links.");
            string[] limitations = [
                "AD-only expected applicability. Not endpoint RSoP or winning setting.",
                "Site association/site GPOs, WMI evaluation, GPP item-level targeting, " +
                "CSE execution, group membership refresh, replication and MDM remain unverified.",
                "User GPO ACL includes the user's Read+Apply and the selected computer's " +
                "GPC Read checks (MS16-072). Client token, SYSVOL share/file " +
                "access, live CSE processing and final precedence stay unverified.",
                "Loopback Replace/Merge are candidates, not confirmed client modes. " +
                    "Normal User OU and computer-OU user policies can be conditional."
            ];
            return JsonSerializer.SerializeToElement(new
            {
                success = true, message = "Unified GPO AD-only applicability (no XML).",
                data = new {
                    domain, dc = actualDc, computer, computerOu,
                    actualComputerOu = machineParent,
                    requestedUser = userName,
                    user = user?.Name ?? "", actualUserOu,
                    userSid = user?.Sid ?? "",
                    computerToken = machine.TokenStatus,
                    userToken = user?.TokenStatus ?? "Not selected",
                    loopback, observedUtc = DateTimeOffset.UtcNow.ToString("O",
                        CultureInfo.InvariantCulture),
                    links = rows, limitations,
                    complete = machineGpos.Complete && normalUserGpos.Complete &&
                        loopbackUserGpos.Complete,
                    provesWinningGpo = false, provesClientApplication = false
                }
            }, Json);
        }

        if (!credentials.IsAlternate) return Run();
        string account = credentials.UserName.Trim();
        string? netbios = null;
        int slash = account.IndexOf('\\');
        if (slash > 0 && slash < account.Length - 1)
        {
            netbios = account[..slash];
            account = account[(slash + 1)..];
        }
        else if (!account.Contains('@')) account += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(account, netbios, credentials.Password, 9, 3,
                out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Alternate network logon for GPO read was rejected.");
        using (token) return WindowsIdentity.RunImpersonated(token, Run);
    }

    private static ScopeSubject ResolveComputer(LdapConnection ldap,
        string selectedOu, string fqdn)
    {
        string filter = "(&(objectCategory=computer)(dNSHostName=" +
            NativeAdDiscovery.EscapeFilter(fqdn) + "))";
        var rows = NativeGpoLifecycleList.SearchPaged(ldap, selectedOu,
            filter, 2, Stopwatch.StartNew(), "distinguishedName", "dNSHostName",
            "userAccountControl");
        if (rows.Count != 1 ||
            !NativeAdDiscovery.Attr(rows[0], "dNSHostName").Equals(fqdn,
                StringComparison.OrdinalIgnoreCase) ||
            !NativeGpoConflictScan.Inside(rows[0].DistinguishedName, selectedOu))
            throw new InvalidDataException("Exact pilot computer missing or ambiguous in selected OU.");
        CheckEnabled(rows[0], "computer");
        return ReadSids(ldap, rows[0].DistinguishedName, fqdn, "computer");
    }

    private static ScopeSubject ResolveUser(LdapConnection ldap, string baseDn,
        string login)
    {
        string escaped = NativeAdDiscovery.EscapeFilter(login);
        string filter = "(&(objectCategory=person)(objectClass=user)(|(sAMAccountName=" +
            escaped + ")(userPrincipalName=" + escaped + ")))";
        var rows = NativeGpoLifecycleList.SearchPaged(ldap, baseDn,
            filter, 2, Stopwatch.StartNew(), "distinguishedName",
            "sAMAccountName", "userPrincipalName", "userAccountControl");
        if (rows.Count != 1 ||
            (!NativeAdDiscovery.Attr(rows[0], "sAMAccountName").Equals(
                login, StringComparison.OrdinalIgnoreCase) &&
            !NativeAdDiscovery.Attr(rows[0], "userPrincipalName").Equals(
                login, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Choose one exact AD user SAM name or UPN.");
        CheckEnabled(rows[0], "user");
        return ReadSids(ldap, rows[0].DistinguishedName,
            NativeAdDiscovery.Attr(rows[0], "sAMAccountName"), "user");
    }

    private static void CheckEnabled(SearchResultEntry entry, string kind)
    {
        string raw = NativeAdDiscovery.Attr(entry, "userAccountControl");
        if (!int.TryParse(raw, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int flags))
            throw new InvalidDataException("Cannot verify " + kind + " account state.");
        if ((flags & 2) != 0)
            throw new InvalidDataException("Selected " + kind + " AD object is disabled.");
    }

    private static ScopeSubject ReadSids(LdapConnection ldap, string dn,
        string name, string kind)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var entry = ReadOne(ldap, dn,
                kind == "computer" ? "(objectCategory=computer)" :
                    "(&(objectCategory=person)(objectClass=user))",
                "objectSid", "tokenGroups");
            if (entry.Attributes["objectSid"] is not { Count: > 0 } sid ||
                sid[0] is not byte[] sidBytes)
                return new ScopeSubject(dn, name, "", "Unavailable", ids);
            string objectSid = new SecurityIdentifier(sidBytes, 0).Value;
            if (entry.Attributes["tokenGroups"] is not { Count: > 0 } groups)
                return new ScopeSubject(dn, name, objectSid, "Unavailable", ids);
            ids.Add(objectSid);
            foreach (object item in groups)
            {
                if (item is not byte[] groupSid)
                    return new ScopeSubject(dn, name, objectSid, "Unavailable",
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                ids.Add(new SecurityIdentifier(groupSid, 0).Value);
            }
            ids.Add("S-1-1-0"); // Everyone
            ids.Add("S-1-5-11"); // Authenticated Users
            return new ScopeSubject(dn, name, objectSid, "Read", ids);
        }
        catch (Exception e) when (e is DirectoryOperationException or
            LdapException or UnauthorizedAccessException or
            ArgumentException or InvalidDataException)
        {
            return new ScopeSubject(dn, name, "", "Unavailable",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static List<ScopeLevel> ReadAncestry(
        LdapConnection ldap, string parentDn, string baseDn)
    {
        if (!NativeGpoConflictScan.Inside(parentDn, baseDn))
            throw new InvalidDataException("AD object is outside selected domain.");
        var dns = new List<string>();
        string? dn = parentDn;
        while (dn is not null && NativeGpoConflictScan.Inside(dn, baseDn))
        {
            if (dns.Count >= 40)
                throw new InvalidDataException("Too many ancestor OUs.");
            dns.Add(dn);
            if (dn.Equals(baseDn, StringComparison.OrdinalIgnoreCase)) break;
            dn = NativeGpoConflictScan.ParentDn(dn);
        }
        if (dns.Count == 0 ||
            !dns[^1].Equals(baseDn, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ancestry does not reach domain root.");
        dns.Reverse();
        var result = new List<ScopeLevel>();
        for (int i = 0; i < dns.Count; i++)
        {
            var entry = ReadOne(ldap, dns[i], "(objectClass=*)",
                "gPLink", "gPOptions");
            var links = NativeGpoLifecycleList.ParseLinks(
                NativeAdDiscovery.Attr(entry, "gPLink"));
            if (links.Count > 100)
                throw new InvalidDataException("Too many policies linked at one level.");
            string raw = NativeAdDiscovery.Attr(entry, "gPOptions");
            if (raw.Length > 0 &&
                (!int.TryParse(raw, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int flags) ||
                 (flags & ~1) != 0))
                throw new InvalidDataException("Unexpected GPO inheritance flags.");
            int.TryParse(raw, out int options);
            result.Add(new ScopeLevel(dns[i], i, (options & 1) != 0, links));
        }
        return result;
    }

    private static ScopedRows ReadScopedLinks(
        LdapConnection ldap, IReadOnlyList<ScopeLevel> levels, ScopeSubject identity,
        string dc, string domain, string baseDn, string scope, string origin,
        ScopeSubject? computerForUser = null)
    {
        if (scope == "User" && computerForUser is null)
            throw new InvalidDataException("User GPO projection needs a computer read token.");
        var rows = new List<ScopedLink>();
        // An empty result or independently excluded GPOs cannot prove that
        // the selected user's/computer's security groups were read.
        bool complete = ScopeTokensVerified(scope, identity.TokenStatus,
            computerForUser?.TokenStatus);
        foreach (var level in levels)
        {
            for (int i = 0; i < level.Links.Count; i++)
            {
                if (rows.Count >= 250)
                    throw new InvalidDataException("Excessive GPO links for a selected principal.");
                var link = level.Links[i];
                if (!NativeGpoConflictScan.TryLinkedGuid(link.Dn, baseDn, out Guid id))
                    throw new InvalidDataException("GPO link points outside selected domain.");
                string display = id.ToString("D");
                string state = "Unknown: source unavailable";
                string security = "Unknown";
                string wmi = "Unknown";
                string computerRead = scope == "User" ? "Not assessed" : "N/A";
                string loopback = "";
                bool enforced = (link.Flags & 2) != 0;
                bool blocked = levels.Any(x => x.Depth > level.Depth && x.Block);
                try
                {
                    var policy = ReadOne(ldap, link.Dn,
                        "(objectClass=groupPolicyContainer)", "displayName",
                        "flags", "versionNumber", "gPCMachineExtensionNames",
                        "gPCWQLFilter");
                    display = NativeAdDiscovery.Attr(policy, "displayName");
                    if (display.Length == 0) display = id.ToString("D");
                    string rawFlags = NativeAdDiscovery.Attr(policy, "flags");
                    if (!TryGpoFlags(rawFlags, out int flags))
                        throw new InvalidDataException(
                            "Malformed or unsupported GPO flags.");
                    wmi = NativeAdDiscovery.Attr(policy, "gPCWQLFilter").Length > 0
                        ? "Unverified" : "None";
                    security = EvaluateSecurity(ldap, link.Dn, identity);
                    state = Classify(link.Flags, blocked, enforced,
                        NativeGpoConflictScan.IsPolicyPartDisabled(
                            flags, scope == "User"), security, wmi);
                    if (scope == "User" && !state.StartsWith("Excluded:",
                            StringComparison.Ordinal))
                    {
                        // Since MS16-072 Windows retrieves User GPOs in the
                        // computer context. This checks GPC Read, not Apply.
                        computerRead = EvaluateSecurity(
                            ldap, link.Dn, computerForUser!, readOnly: true);
                        state = QualifyComputerRead(state, computerRead);
                        if (computerRead == "Unknown") complete = false;
                    }
                    if (scope == "Computer" && !state.StartsWith("Excluded:",
                            StringComparison.Ordinal) &&
                        !NativeGpoConflictScan.IsPolicyPartDisabled(flags, false))
                    {
                        string cse = NativeAdDiscovery.Attr(policy,
                            "gPCMachineExtensionNames");
                        string v = NativeAdDiscovery.Attr(policy, "versionNumber");
                        if (!int.TryParse(v, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int version))
                            version = 0;
                        if (NativeGpoConflictScan.PolicyHalfHasConfiguredSettings(
                                version, cse, false))
                        {
                            loopback = ReadLoopbackFromSource(dc, domain, id,
                                out bool read);
                            if (!read) complete = false;
                        }
                    }
                    if (state.StartsWith("Unknown:", StringComparison.Ordinal))
                        complete = false;
                }
                catch (Exception e) when (e is DirectoryOperationException or
                    LdapException or InvalidDataException or IOException or
                    UnauthorizedAccessException or ArgumentException)
                {
                    complete = false;
                    state = "Unknown: unreadable GPO/ACL";
                }
                rows.Add(new ScopedLink(Safe(display, 150), id.ToString("D"),
                    scope, origin, Safe(level.Dn, 2048),
                    level.Depth, level.Links.Count - i, enforced, state,
                    security, wmi, loopback, computerRead));
            }
        }
        return new ScopedRows(rows, complete);
    }

    // AD permits an absent GPC flags attribute; it means both halves enabled.
    // Explicit malformed or out-of-range values must never be read as 0.
    internal static bool TryGpoFlags(string raw, out int flags)
    {
        flags = 0;
        return raw.Length == 0 ||
            (int.TryParse(raw, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out flags) && flags is >= 0 and <= 3);
    }

    // Read-only tokenGroups evidence is essential for an AD-only scope result.
    // A zero-link or all-excluded scan is incomplete if either token is missing.
    internal static bool ScopeTokensVerified(string scope, string identityToken,
        string? computerToken) =>
        identityToken == "Read" &&
        (scope == "Computer" || (scope == "User" && computerToken == "Read"));

    // MS16-072: a verified computer GPC Read deny blocks the User policy
    // even when the selected user's Read+Apply rights are unresolved.
    // An independently excluded link remains excluded for its original cause.
    internal static string QualifyComputerRead(string state, string read)
    {
        if (state.StartsWith("Excluded:", StringComparison.Ordinal))
            return state;
        if (read == "Deny" && state is ("AD candidate" or
                "Conditional: WMI not evaluated" or "Unknown: security filtering"))
            return "Excluded: computer GPO Read denied";
        if (state is not ("AD candidate" or "Conditional: WMI not evaluated"))
            return state;
        return read == "Read" ? state :
            "Conditional: computer Read not verified";
    }

    // The selected DC never proves that the endpoint's loopback mode ran.
    internal static string QualifyLoopbackUserRow(
        string state, string mode, bool fromComputerOu)
    {
        if (state is not ("AD candidate" or "Conditional: WMI not evaluated"))
            return state; // Preserve exclusions and unknown ACL/source states.
        if (fromComputerOu)
            return "Conditional: loopback not confirmed";
        return mode switch {
            "Replace candidate" => "Conditional: loopback Replace candidate",
            "Unknown" => "Conditional: loopback unknown",
            _ => state
        };
    }

    private static string Classify(int linkFlags, bool blocked, bool enforced,
        bool halfDisabled, string security, string wmi)
    {
        if ((linkFlags & 1) != 0) return "Excluded: link disabled";
        if (blocked && !enforced) return "Excluded: block inheritance";
        if (halfDisabled) return "Excluded: policy half disabled";
        if (security == "Deny") return "Excluded: matching security deny";
        if (security != "Read+Apply") return "Unknown: security filtering";
        return wmi == "None" ? "AD candidate" : "Conditional: WMI not evaluated";
    }

    // GPC Read needs directory property access; READ_CONTROL (0x20000)
    // alone is NOT enough, even though it allows reading the descriptor.
    internal static bool GrantsGpcReadFromAccessMask(int rights) =>
        (rights & 0x10000000) != 0 ||
        (rights & 0x80000000) != 0 ||
        (rights & 0x00000010) != 0;

    private static string EvaluateSecurity(
        LdapConnection ldap, string policyDn, ScopeSubject subject,
        bool readOnly = false)
    {
        if (subject.TokenStatus != "Read" || subject.TokenSids.Count == 0)
            return "Unknown";
        try
        {
            var request = new SearchRequest(policyDn,
                "(objectClass=groupPolicyContainer)", SearchScope.Base,
                "nTSecurityDescriptor");
            request.Controls.Add(new SecurityDescriptorFlagControl(SecurityMasks.Dacl));
            var response = (SearchResponse)ldap.SendRequest(request);
            if (response.Entries.Count != 1 ||
                response.Entries[0].Attributes["nTSecurityDescriptor"] is
                    not { Count: > 0 } attr || attr[0] is not byte[] data)
                return "Unknown";
            var acl = new RawSecurityDescriptor(data, 0).DiscretionaryAcl;
            if (acl is null) return "Unknown";
            return ClassifyScopeDacl(acl, subject.TokenSids, readOnly);
        }
        catch (Exception e) when (e is DirectoryOperationException or
            LdapException or InvalidDataException or ArgumentException or
            UnauthorizedAccessException)
        { return "Unknown"; }
    }

    // This is a conservative AD-only estimate, not AccessCheck on the actual
    // endpoint token. Conditional ACEs depend on claims/context not present in
    // tokenGroups; a property-specific Read deny cannot be ignored simply
    // because a different ACE grants generic Read.
    internal static string ClassifyScopeDacl(RawAcl acl,
        IReadOnlySet<string> tokenSids, bool readOnly = false)
    {
        if (tokenSids.Count == 0) return "Unknown";
        bool readAllow = false, applyAllow = false;
        bool readDeny = false, applyDeny = false;
        bool ambiguous = false;
        foreach (GenericAce ace in acl)
        {
            if ((ace.AceFlags & AceFlags.InheritOnly) != 0 ||
                ace is not QualifiedAce q ||
                !tokenSids.Contains(q.SecurityIdentifier.Value) ||
                q.AceQualifier is not (AceQualifier.AccessAllowed or AceQualifier.AccessDenied))
                continue;

            int rights = q.AccessMask;
            bool read = GrantsGpcReadFromAccessMask(rights);
            bool apply = (rights & 0x10000000) != 0 ||
                (rights & 0x00000100) != 0;
            bool denied = q.AceQualifier == AceQualifier.AccessDenied;

            if (q is ObjectAce objectAce &&
                (objectAce.ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent) != 0)
            {
                if (objectAce.ObjectAceType != ScopeApplyRight)
                    apply = false;
                // Object/property-specific Read is not equivalent to full GPC
                // Read. A deny may restrict a required attribute; we do not
                // have schema/attribute-level effective access evidence here.
                if (read && denied) ambiguous = true;
                read = false;
            }
            if (readOnly) apply = false;
            if (!read && !apply) continue;

            if (q.IsCallback)
            {
                ambiguous = true;
                continue;
            }

            if (denied)
            {
                // A preceding applicable allow may have satisfied part of the
                // access request already. Do not claim a conclusive deny from
                // a simplified aggregate when DACL order matters.
                if ((read && readAllow) || (apply && applyAllow))
                    ambiguous = true;
                readDeny |= read;
                applyDeny |= apply;
            }
            else
            {
                readAllow |= read;
                applyAllow |= apply;
            }
        }
        if (ambiguous) return "Unknown";
        if (readOnly)
            return readDeny ? "Deny" : readAllow ? "Read" : "Unknown";
        return readDeny || applyDeny ? "Deny" :
            readAllow && applyAllow ? "Read+Apply" : "Unknown";
    }

    /// <summary>Only the loopback setting in MACHINE Registry.pol is consulted;
    /// the complete known-setting scanner is still NativeGpoDomainAudit.AllGpos.</summary>
    private static string ReadLoopbackFromSource(
        string dc, string domain, Guid id, out bool reliable)
    {
        reliable = false;
        string directory = Path.Combine(@"\\", dc, "SYSVOL", domain,
            "Policies", "{" + id.ToString("D").ToUpperInvariant() + "}", "Machine");
        if (!NativeGpoConflictScan.PolicyFolderExistsSafe(directory))
            return "";
        NativeGpoConflictScan.EnsureDirectory(directory);
        string file = Path.Combine(directory, "Registry.pol");
        if (!NativeGpoConflictScan.FileExistsSafe(file))
        {
            reliable = true;
            return "";
        }
        var hits = NativeGpoConflictScan.ParseRegistryPol(
            File.ReadAllBytes(file), false).Where(x =>
                x.key.Equals(@"Software\Policies\Microsoft\Windows\System",
                    StringComparison.OrdinalIgnoreCase) &&
                x.name.Equals("UserPolicyMode", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        reliable = hits.Length <= 1;
        return hits.Length == 1 ? hits[0].value.Trim() : "";
    }

    private static string DecideLoopback(IReadOnlyList<ScopedLink> machine,
        bool complete)
    {
        var rows = machine.Where(x => x.LoopbackValue.Length > 0 &&
            !x.State.StartsWith("Excluded:", StringComparison.Ordinal)).ToArray();
        if (!complete || rows.Any(x => x.State != "AD candidate"))
            return "Unknown";
        if (rows.Length == 0) return "Not configured";
        // Without a proven setting precedence never choose one of mixed modes.
        string[] values = rows.Select(x => x.LoopbackValue)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length != 1) return "Unknown";
        return values[0] switch { "0" => "Not configured", "1" => "Merge candidate",
            "2" => "Replace candidate", _ => "Unknown" };
    }

    private static SearchResultEntry ReadOne(
        LdapConnection ldap, string dn, string filter, params string[] attrs)
    {
        var response = (SearchResponse)ldap.SendRequest(
            new SearchRequest(dn, filter, SearchScope.Base, attrs));
        if (response.Entries.Count != 1)
            throw new InvalidDataException("AD object/GPO missing or unreadable.");
        return response.Entries[0];
    }
}
