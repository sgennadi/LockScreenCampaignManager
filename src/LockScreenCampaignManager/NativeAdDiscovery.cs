using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only AD connection and inventory using native .NET LDAP APIs.
/// No PowerShell, RSAT, WinRM or AD/GPO writes. Alternate credentials live
/// only in memory. Every connection is authenticated, signed and sealed.
/// </summary>
internal static class NativeAdDiscovery
{
    private const int QueryLimit = 5000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static JsonElement Execute(string mode, object request, DomainCredentials credentials)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AD discovery requires Windows integrated LDAP authentication.");

        JsonElement args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Get(args, "domain").Trim().ToLowerInvariant();
        if (domain.Length == 0) domain = DcDnsDiscovery.LocalDnsDomain().ToLowerInvariant();
        string preferredDc = Get(args, "dc").Trim();
        ValidateNames(domain, preferredDc);

        // Bind to the explicitly selected DC, or resolve one using Windows DC
        // locator. Never silently change a user-selected server.
        string server = preferredDc.Length > 0
            ? preferredDc : DcDnsDiscovery.FindControllers(domain).FirstOrDefault() ?? domain;
        using var ldap = Connect(server, credentials);
        var root = ReadRootDse(ldap);
        string baseDn = Attr(root, "defaultNamingContext");
        string actualDc = Attr(root, "dnsHostName");
        string discoveredDomain = DomainFromDn(baseDn);
        if (!string.Equals(discoveredDomain, domain, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Selected domain controller belongs to another AD domain.");
        if (string.IsNullOrWhiteSpace(actualDc))
            throw new InvalidOperationException("LDAP RootDSE did not return a controller DNS name.");
        // Unknown RODC metadata must never be reported as write-capable.
        bool writable = Attr(root, "isRODC").Equals("FALSE", StringComparison.OrdinalIgnoreCase);

        object data = mode switch
        {
            "Connect" => new
            {
                domain = discoveredDomain, dc = actualDc, baseDn,
                credentialContext = credentials.IsAlternate ? credentials.UserName : Environment.UserName,
                gpoToolsAvailable = true, rsatRequired = false,
                deploymentMode = "Native .NET LDAP read-only discovery (no RSAT/WinRM)",
                dcWritable = writable
            },
            "Snapshot" => Snapshot(ldap, discoveredDomain, actualDc, baseDn, writable),
            "DomainControllers" => Controllers(ldap, baseDn, actualDc, writable),
            "OUs" => OrganizationalUnits(ldap, baseDn),
            "Groups" => Groups(ldap, baseDn),
            "Users" => Users(ldap, baseDn, Get(args, "search")),
            "Permissions" => Permissions(ldap, args, credentials, discoveredDomain,
                actualDc, baseDn, writable),
            _ => throw new ArgumentException("Unsupported read-only directory operation.", nameof(mode))
        };
        return JsonSerializer.SerializeToElement(new
        {
            success = true,
            message = "Signed and sealed native LDAP discovery succeeded; no PowerShell or RSAT.",
            data
        }, Json);
    }

    internal static void ValidateNames(string domain, string preferredDc)
    {
        if (!Regex.IsMatch(domain, @"\A(?=.{3,253}\z)[a-z0-9-]+(?:\.[a-z0-9-]+)+\z",
                RegexOptions.CultureInvariant))
            throw new ArgumentException("Enter a valid AD DNS domain.", nameof(domain));
        if (preferredDc.Length > 0 &&
            (!Regex.IsMatch(preferredDc, @"\A[a-zA-Z0-9-]{1,63}(?:\.[a-zA-Z0-9-]{1,63})*\z",
                RegexOptions.CultureInvariant) ||
             IPAddress.TryParse(preferredDc, out _) ||
             preferredDc.Length > 253))
            throw new ArgumentException("Enter a DC DNS hostname, not an IP address.", nameof(preferredDc));
    }

    internal static string EscapeFilter(string value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        return value.Replace(@"\", @"\5c", StringComparison.Ordinal)
            .Replace("*", @"\2a", StringComparison.Ordinal)
            .Replace("(", @"\28", StringComparison.Ordinal)
            .Replace(")", @"\29", StringComparison.Ordinal)
            .Replace("\0", @"\00", StringComparison.Ordinal);
    }

    internal static string DomainFromDn(string baseDn)
    {
        string[] components = baseDn.Split(',', StringSplitOptions.TrimEntries);
        if (components.Length < 2 ||
            components.Any(x => !x.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Unexpected AD RootDSE naming context.");
        return string.Join(".", components.Select(x => x[3..])).ToLowerInvariant();
    }

    private static string Get(JsonElement request, string name) =>
        request.ValueKind == JsonValueKind.Object &&
        request.TryGetProperty(name, out var item) &&
        item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";

    internal static LdapConnection Connect(string server, DomainCredentials credentials)
    {
        var identifier = new LdapDirectoryIdentifier(server, 389, false, false);
        LdapConnection ldap;
        if (credentials.IsAlternate)
        {
            if (string.IsNullOrWhiteSpace(credentials.Password))
                throw new ArgumentException("Enter the alternate AD account password.");
            string login = credentials.UserName.Trim();
            string? netbios = null;
            int slash = login.IndexOf('\\');
            if (slash > 0 && slash < login.Length - 1)
            {
                netbios = login[..slash];
                login = login[(slash + 1)..];
            }
            else if (!login.Contains('@'))
                login += "@" + credentials.Domain.Trim();
            ldap = new LdapConnection(identifier,
                new NetworkCredential(login, credentials.Password, netbios),
                AuthType.Negotiate);
        }
        else
            ldap = new LdapConnection(identifier) { AuthType = AuthType.Negotiate };
        try
        {
            ldap.SessionOptions.ProtocolVersion = 3;
            ldap.SessionOptions.Signing = true;
            ldap.SessionOptions.Sealing = true;
            ldap.Timeout = TimeSpan.FromSeconds(25);
            ldap.Bind();
            return ldap;
        }
        catch
        {
            ldap.Dispose();
            throw;
        }
    }

    internal static SearchResultEntry ReadRootDse(LdapConnection ldap)
    {
        var response = (SearchResponse)ldap.SendRequest(new SearchRequest("",
            "(objectClass=*)", SearchScope.Base,
            "defaultNamingContext", "dnsHostName", "isRODC", "currentTime",
            "configurationNamingContext"));
        if (response.Entries.Count != 1)
            throw new InvalidOperationException("RootDSE did not return exactly one directory root.");
        return response.Entries[0];
    }

    internal static string Attr(SearchResultEntry entry, string name)
    {
        if (!entry.Attributes.Contains(name)) return "";
        var attr = entry.Attributes[name];
        return attr is not null && attr.Count > 0 ? attr[0]?.ToString() ?? "" : "";
    }

    private static byte[] BinaryAttr(SearchResultEntry entry, string name)
    {
        if (!entry.Attributes.Contains(name))
            throw new InvalidOperationException("Directory permission metadata is unavailable.");
        var attr = entry.Attributes[name];
        if (attr is null || attr.Count == 0 || attr[0] is not byte[] bytes)
            throw new InvalidOperationException("Directory permission metadata is invalid.");
        return bytes;
    }

    private static List<SearchResultEntry> Search(LdapConnection ldap, string dn,
        string filter, int max, params string[] attributes)
    {
        var result = new List<SearchResultEntry>();
        byte[] cookie = Array.Empty<byte>();
        do
        {
            var request = new SearchRequest(dn, filter, SearchScope.Subtree, attributes);
            request.Controls.Add(new PageResultRequestControl(Math.Min(500, max))
            {
                Cookie = cookie
            });
            var response = (SearchResponse)ldap.SendRequest(request);
            foreach (SearchResultEntry entry in response.Entries)
            {
                result.Add(entry);
                if (result.Count >= max) return result;
            }
            cookie = Array.Empty<byte>();
            foreach (DirectoryControl control in response.Controls)
                if (control is PageResultResponseControl page)
                    cookie = page.Cookie ?? Array.Empty<byte>();
        }
        while (cookie.Length > 0);
        return result;
    }

    private static object[] Controllers(LdapConnection ldap, string baseDn,
        string actualDc, bool writable)
    {
        var rows = Search(ldap, baseDn,
            "(&(objectCategory=computer)(|(primaryGroupID=516)(primaryGroupID=521)))",
            QueryLimit, "dNSHostName", "primaryGroupID");
        var controllers = rows.Select(x => new
        {
            name = Attr(x, "dNSHostName"),
            site = "",
            writable = Attr(x, "primaryGroupID") != "521"
        }).Where(x => x.name.Length > 0).OrderBy(x => x.name,
            StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray();
        return controllers.Length > 0 ? controllers :
            new object[] { new { name = actualDc, site = "", writable } };
    }

    private static object[] OrganizationalUnits(LdapConnection ldap, string baseDn) =>
        Search(ldap, baseDn, "(objectCategory=organizationalUnit)", QueryLimit,
            "name", "distinguishedName")
        .Select(x => new { name = Attr(x, "name"), dn = x.DistinguishedName })
        .OrderBy(x => x.dn, StringComparer.OrdinalIgnoreCase)
        .Cast<object>().ToArray();

    private static object[] Groups(LdapConnection ldap, string baseDn) =>
        Search(ldap, baseDn,
            "(&(objectCategory=group)(groupType:1.2.840.113556.1.4.803:=2147483648))",
            QueryLimit, "name", "sAMAccountName", "distinguishedName")
        .Select(x => new { name = Attr(x, "name"),
            sam = Attr(x, "sAMAccountName"), dn = x.DistinguishedName })
        .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)
        .Cast<object>().ToArray();

    private static object[] Users(LdapConnection ldap, string baseDn, string search)
    {
        search = search.Trim();
        if (search.Length is < 2 or > 48)
            throw new ArgumentException("Search must contain 2–48 characters.");
        string term = EscapeFilter(search);
        string filter = "(&(objectCategory=person)(objectClass=user)" +
            "(|(sAMAccountName=*" + term + "*)(displayName=*" + term + "*)))";
        return Search(ldap, baseDn, filter, 75,
            "name", "sAMAccountName", "distinguishedName")
            .Select(x => new { name = Attr(x, "name"),
                sam = Attr(x, "sAMAccountName"), dn = x.DistinguishedName })
            .OrderBy(x => x.sam, StringComparer.OrdinalIgnoreCase)
            .Cast<object>().ToArray();
    }

    private static object Snapshot(LdapConnection ldap, string domain,
        string dc, string baseDn, bool writable)
    {
        var computers = Search(ldap, baseDn,
            "(&(objectCategory=computer)(|(primaryGroupID=516)(primaryGroupID=521)))",
            QueryLimit, "dNSHostName", "primaryGroupID");
        var ous = Search(ldap, baseDn, "(objectCategory=organizationalUnit)",
            QueryLimit, "name", "distinguishedName");
        var groups = Search(ldap, baseDn,
            "(&(objectCategory=group)(groupType:1.2.840.113556.1.4.803:=2147483648))",
            QueryLimit, "name", "sAMAccountName", "distinguishedName");
        var policies = Search(ldap, "CN=Policies,CN=System," + baseDn,
            "(objectClass=groupPolicyContainer)", QueryLimit, "displayName", "name");
        var dcList = computers.Select(x => new
        {
            name = Attr(x, "dNSHostName"),
            writable = Attr(x, "primaryGroupID") != "521"
        }).Where(x => x.name.Length > 0).OrderBy(x => x.name,
            StringComparer.OrdinalIgnoreCase).ToArray();
        object[] controllers = dcList.Length > 0 ?
            dcList.Cast<object>().ToArray() :
            new object[] { new { name = dc, writable } };
        return new
        {
            domain, dc, baseDn, dcWritable = writable,
            readOnly = true, observedUtc = DateTimeOffset.UtcNow.ToString("O"),
            capped = computers.Count >= QueryLimit || ous.Count >= QueryLimit ||
                groups.Count >= QueryLimit || policies.Count >= QueryLimit,
            controllers,
            ous = ous.Select(x => new { name = Attr(x, "name"),
                dn = x.DistinguishedName }).OrderBy(x => x.dn, StringComparer.OrdinalIgnoreCase).ToArray(),
            groups = groups.Select(x => new { name = Attr(x, "name"),
                sam = Attr(x, "sAMAccountName"), dn = x.DistinguishedName })
                .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase).ToArray(),
            gpos = policies.Select(x => new { name = Attr(x, "displayName"),
                id = Attr(x, "name") }).OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static object Permissions(LdapConnection ldap, JsonElement request,
        DomainCredentials credentials, string domain, string dc, string baseDn, bool writable)
    {
        string targetOu = Get(request, "ouDn").Trim();
        // An initial connection may have no OU yet. Assess the domain-wide
        // GPO creation ACL now; check OU linking only once a scope is selected.
        bool hasOu = targetOu.Length > 0;
        if (hasOu && (!targetOu.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
            !targetOu.EndsWith("," + baseDn, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Select an OU inside the connected AD domain.");
        // Assess the account used for this LDAP connection, never an
        // arbitrary user name supplied in an untrusted request payload.
        string login = credentials.IsAlternate
            ? credentials.UserName.Trim()
            : WindowsIdentity.GetCurrent().Name;
        string effectiveLogin = login.Contains('\\') ? login[(login.LastIndexOf('\\') + 1)..] : login;
        string key = effectiveLogin.Contains('@') ? "userPrincipalName" : "sAMAccountName";
        string filter = "(&(objectCategory=person)(objectClass=user)(" +
            key + "=" + EscapeFilter(effectiveLogin) + "))";
        var users = Search(ldap, baseDn, filter, 2, "objectSid", "distinguishedName");
        if (users.Count != 1)
            throw new InvalidOperationException("Cannot resolve exactly one AD account for permission checks.");

        // tokenGroups is a computed attribute: request it directly on the
        // resolved user (BASE search), not on a subtree enumeration. Without
        // complete group SIDs the result must remain UNKNOWN rather than DENIED.
        var tokenRequest = new SearchRequest(users[0].DistinguishedName,
            "(objectClass=user)", SearchScope.Base, "tokenGroups");
        var tokenResponse = (SearchResponse)ldap.SendRequest(tokenRequest);
        bool tokensComplete = tokenResponse.Entries.Count == 1 &&
            tokenResponse.Entries[0].Attributes.Contains("tokenGroups");
        var principals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "S-1-1-0", "S-1-5-11" };
        principals.Add(new SecurityIdentifier(BinaryAttr(users[0], "objectSid"), 0).Value);
        if (tokensComplete)
        {
            var tokens = tokenResponse.Entries[0].Attributes["tokenGroups"];
            if (tokens is null) tokensComplete = false;
            else
                foreach (object token in tokens)
                {
                    if (token is not byte[] binary)
                    {
                        tokensComplete = false;
                        break;
                    }
                    principals.Add(new SecurityIdentifier(binary, 0).Value);
                }
        }

        string create = "UNKNOWN_INCOMPLETE_TOKEN";
        string link = hasOu ? "UNKNOWN_INCOMPLETE_TOKEN" : "REQUIRES_OU_SELECTION";
        if (tokensComplete)
        {
            var policiesAcl = ReadAcl(ldap, "CN=Policies,CN=System," + baseDn);
            create = EffectiveAclHint(policiesAcl, principals, 1,
                Guid.Parse("f30e3bc2-9ff0-11d1-b603-0000f80367c1"));
            if (hasOu)
                link = EffectiveAclHint(ReadAcl(ldap, targetOu), principals, 32,
                    Guid.Parse("f30e3bc1-9ff0-11d1-b603-0000f80367c1"));
        }
        return new
        {
            domain, dc, user = login, readOnly = true, dcWritable = writable,
            gpoCreateAce = create, ouGpoLinkAce = link,
            groupTokenComplete = tokensComplete,
            sysvolWrite = "NOT_CHECKED_NO_WRITES", fullPermissionVerified = false,
            message = "Read-only ACE hints only; GPO COM and SYSVOL rights remain unverified."
        };
    }

    private static RawSecurityDescriptor ReadAcl(LdapConnection ldap, string dn)
    {
        var request = new SearchRequest(dn, "(objectClass=*)",
            SearchScope.Base, "nTSecurityDescriptor");
        request.Controls.Add(new SecurityDescriptorFlagControl(SecurityMasks.Dacl));
        var result = (SearchResponse)ldap.SendRequest(request);
        if (result.Entries.Count != 1)
            throw new InvalidOperationException("AD ACL query returned no unique object.");
        return new RawSecurityDescriptor(BinaryAttr(result.Entries[0], "nTSecurityDescriptor"), 0);
    }

    private static string EffectiveAclHint(RawSecurityDescriptor acl,
        HashSet<string> sids, int mask, Guid objectType)
    {
        bool allow = false;
        bool deny = false;
        if (acl.DiscretionaryAcl is null) return "NO_MATCHING_ALLOW";
        foreach (GenericAce ace in acl.DiscretionaryAcl)
        {
            // Inherit-only ACEs govern descendants, not this target object.
            if ((ace.AceFlags & AceFlags.InheritOnly) != 0)
                continue;
            if (ace is not QualifiedAce qualified ||
                !sids.Contains(qualified.SecurityIdentifier.Value))
                continue;
            if ((qualified.AccessMask & 0x10000000) == 0 &&
                (qualified.AccessMask & mask) == 0)
                continue;
            if (ace is ObjectAce obj &&
                (obj.ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent) != 0 &&
                obj.ObjectAceType != objectType)
                continue;
            if (qualified.AceQualifier == AceQualifier.AccessDenied) deny = true;
            if (qualified.AceQualifier == AceQualifier.AccessAllowed) allow = true;
        }
        return deny ? "POSSIBLE_DENY" : allow ? "POTENTIAL_ALLOW" : "NO_MATCHING_ALLOW";
    }
}
