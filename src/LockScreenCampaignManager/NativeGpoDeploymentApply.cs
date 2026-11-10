using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Native, fail-closed initial campaign deployment: stage verified SYSVOL
/// payload, create a fresh GPO via Windows COM, restrict Apply Group Policy
/// to the selected security group, register the startup script, then link
/// ONLY the explicitly approved OU. Failure preserves any unlinked GPO for
/// manual inspection. No RSAT, WinRM or PowerShell process is used.
/// </summary>
internal static class NativeGpoDeploymentApply
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex ImageName = new(
        @"\A[0-9]{2}\.jpg\z", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex GptZero = new(
        @"(?m)^Version=0[ \t]*\r?$", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));
    private static readonly Guid ApplyPolicy = new(
        "edacfd8f-ffb3-11d1-b41d-00a0c968f939");
    private const string MachineExtension =
        "[{42B5FAAE-6536-11D2-AE5A-0000F87571E3}{40B6664F-4972-11D1-A7CA-0000F87571E3}]";

    internal static JsonElement Execute(string mode, object request,
        DomainCredentials credentials)
    {
        if (mode != "Deploy")
            throw new ArgumentException("Only GPO Deploy Apply is supported.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows LDAP, GPO COM and SMB are required.");

        JsonElement args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Get(args, "domain").Trim().ToLowerInvariant();
        string dc = Get(args, "dc").Trim();
        string ouDn = Get(args, "ouDn").Trim();
        string groupDn = Get(args, "groupDn").Trim();
        string name = Get(args, "gpoName").Trim();
        string packageFolder = Get(args, "packagePath").Trim();

        JsonElement Run()
        {
            // Re-run the full native Plan independently during the Apply call.
            JsonElement fresh = NativeGpoDeploymentPlan.Execute(
                "Plan", request, new DomainCredentials(domain, "", ""));
            var plan = fresh.GetProperty("data");
            string actualDc = Required(plan, "dc");
            string id = Required(plan, "campaignId");
            string targetPath = Required(plan, "packageStoragePath");
            string clientScript = Required(plan, "clientScriptPath");
            CampaignContentSource contentSource = CampaignPayloadLocation.Parse(
                Required(plan, "contentSource"));
            string smbRoot = plan.GetProperty("contentShareRoot").GetString() ?? "";
            CampaignClientType clientType = NativeGpoDeploymentPlan.ParseClientType(
                Required(plan, "clientType"));
            if (NativeGpoDeploymentPlan.ParseClientType(Get(args, "clientType")) != clientType)
                throw new InvalidOperationException("Client type changed after preflight.");
            // Defense in depth: callers cannot bypass the UI and deploy an
            // unvalidated native client through the operation dispatcher.
            if (clientType == CampaignClientType.Executable &&
                !NativeClientAgent.CanDeployNativeClient)
                throw new NotSupportedException(
                    "Native EXE GPO deployment requires an explicit isolated-lab opt-in.");
            int imageCount = plan.GetProperty("imageCount").GetInt32();
            string expectedManifestHash = Required(plan, "manifestSha256");
            string expectedAgentHash = Required(plan, "agentSha256");
            if (!NativeSysvolStorage.ValidCampaignId(id) ||
                imageCount is < 1 or > 30)
                throw new InvalidDataException("The deployment package is not valid.");

            using var ldap = NativeAdDiscovery.Connect(actualDc,
                new DomainCredentials(domain, "", ""));
            ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            var root = NativeAdDiscovery.ReadRootDse(ldap);
            string baseDn = NativeAdDiscovery.Attr(root, "defaultNamingContext");
            string observedDc = NativeAdDiscovery.Attr(root, "dnsHostName");
            if (!NativeGpoLifecycleList.SameController(actualDc, observedDc) ||
                !NativeAdDiscovery.DomainFromDn(baseDn).Equals(domain,
                    StringComparison.OrdinalIgnoreCase) ||
                NativeAdDiscovery.ReadDcWritable(ldap, root) != true)
                throw new InvalidOperationException("Selected writable DC changed or could not be verified.");

            // Do not let the explicit OU, security group or newly chosen
            // display name drift after the original read-only Plan.
            if (!string.Equals(Get(args, "ouDn").Trim(), Required(plan, "ou"),
                    StringComparison.Ordinal) ||
                !string.Equals(Get(args, "groupDn").Trim(), Required(plan, "group"),
                    StringComparison.Ordinal) ||
                !string.Equals(name, Required(plan, "gpoName"),
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Deployment target changed after validation.");

            var group = One(ldap, groupDn, "(objectClass=group)", "objectSid",
                "groupType");
            if (group.Attributes["objectSid"] is not { Count: > 0 } sidValue ||
                sidValue[0] is not byte[] sidBytes)
                throw new InvalidDataException("Selected security group SID is unavailable.");
            var selectedSid = new SecurityIdentifier(sidBytes, 0);
            if (!long.TryParse(NativeAdDiscovery.Attr(group, "groupType"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out long groupType) || (groupType & 0x80000000L) == 0)
                throw new InvalidDataException("Selected group is not security-enabled.");
            _ = One(ldap, ouDn, "(objectClass=organizationalUnit)", "distinguishedName");
            bool labCanary = clientType == CampaignClientType.Executable &&
                !NativeClientAgent.IsReadyForProduction;
            if (labCanary)
                ValidateLabCanaryGroup(ldap, groupDn, ouDn,
                    plan.GetProperty("directGroupMembers").GetInt32());
            var currentGpos = NativeGpoLifecycleList.SearchPaged(ldap,
                "CN=Policies,CN=System," + baseDn,
                "(objectClass=groupPolicyContainer)", 2500,
                Stopwatch.StartNew(), "displayName");
            if (currentGpos.Any(x => NativeAdDiscovery.Attr(x, "displayName")
                .Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("GPO name is already in use.");
            if (Directory.Exists(targetPath))
                throw new InvalidOperationException("Campaign payload already exists; never overwrite.");

            string basePath = Path.GetDirectoryName(targetPath) ??
                throw new InvalidDataException("Invalid SYSVOL source path.");
            string stage = Path.Combine(basePath, ".staging-" + Guid.NewGuid().ToString("N"));
            Guid? created = null;
            bool linked = false;
            bool moved = false;
            try
            {
                if (contentSource == CampaignContentSource.SmbShare)
                {
                    if (!string.Equals(basePath, smbRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Approved SMB root changed.");
                    // An SMB share must already exist and be admin-controlled.
                    // Do NOT create new shares or parent folders during Deploy.
                    CampaignPayloadLocation.CheckShareReadOnly(smbRoot);
                }
                else Directory.CreateDirectory(basePath);
                Directory.CreateDirectory(Path.Combine(stage, "images"));
                CopyValidatedPackage(packageFolder, stage);
                if (Directory.Exists(targetPath))
                    throw new IOException("Campaign payload target appeared during staging.");
                Directory.Move(stage, targetPath);
                moved = true;
                if (contentSource == CampaignContentSource.SmbShare)
                {
                    CampaignPayloadLocation.CheckShareReadOnly(targetPath);
                    var verified = NativeGpoDeploymentPlan.VerifyImmutablePackageForTest(targetPath);
                    if (verified.Id != id ||
                        !verified.ManifestSha256.Equals(expectedManifestHash,
                            StringComparison.OrdinalIgnoreCase) ||
                        !verified.AgentSha256.Equals(expectedAgentHash,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            "Published SMB package differs from preflight SHA-256 identity.");
                }

                // Create the new policy only after all source files are safely
                // staged and hashes have been checked.
                created = NativeGpoCom.CreateNewGpo(actualDc, name);
                string gpoDn = NativeGpoLifecycleList.PolicyDn(created.Value, baseDn);
                var newGpo = One(ldap, gpoDn, "(objectClass=groupPolicyContainer)",
                    "versionNumber", "gPCMachineExtensionNames");
                if (NativeAdDiscovery.Attr(newGpo, "versionNumber") != "0")
                    throw new InvalidDataException("New GPO version is not zero.");
                if (NativeAdDiscovery.Attr(newGpo, "gPCMachineExtensionNames").Length != 0)
                    throw new InvalidDataException("New GPO already has machine extensions.");

                RestrictApplyToGroup(ldap, gpoDn, selectedSid);
                if (contentSource == CampaignContentSource.SmbShare)
                {
                    CampaignPayloadLocation.CheckShareReadOnly(targetPath);
                    var lastCheck = NativeGpoDeploymentPlan.VerifyImmutablePackageForTest(targetPath);
                    if (!lastCheck.ManifestSha256.Equals(expectedManifestHash,
                            StringComparison.OrdinalIgnoreCase) ||
                        !lastCheck.AgentSha256.Equals(expectedAgentHash,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            "SMB source changed before GPO startup registration.");
                }
                RegisterStartup(ldap, gpoDn, actualDc, domain,
                    created.Value, clientScript, clientType);
                // Membership can drift while files and GPO are being staged.
                // Check once more immediately before linking into the OU.
                if (labCanary)
                    ValidateLabCanaryGroup(ldap, groupDn, ouDn,
                        plan.GetProperty("directGroupMembers").GetInt32());
                LinkNewGpo(ldap, ouDn, gpoDn);
                linked = true;
                return JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    message = "New GPO created and scoped; startup registered, selected OU linked via native C#.",
                    data = new
                    {
                        domain, dc = actualDc, ou = ouDn, group = groupDn,
                        groupSam = Required(plan, "groupSam"),
                        directGroupMembers = plan.GetProperty("directGroupMembers").GetInt32(),
                        gpoName = name, campaignId = id,
                        imageCount, imageBytes = plan.GetProperty("imageBytes").GetInt64(),
                        sysvolOnDc = contentSource == CampaignContentSource.Sysvol ? targetPath : "",
                        packageStoragePath = targetPath,
                        contentSource = contentSource.ToString(),
                        contentShareRoot = smbRoot,
                        manifestSha256 = expectedManifestHash,
                        agentSha256 = expectedAgentHash,
                        clientScriptPath = clientScript,
                        clientType = clientType.ToString(),
                        gpoMethod = clientType == CampaignClientType.Executable
                            ? "ClassicStartupExe" : "PowerShellStartup",
                        operation = mode, rsatRequired = false,
                        nativeEngine = "C# LDAP + Windows GPO COM + SMB",
                        dcUtc = Required(plan, "dcUtc"),
                        clockSkewSeconds = plan.GetProperty("clockSkewSeconds").GetInt32(),
                        gpoId = created.Value.ToString("D"), linked
                    }
                }, Json);
            }
            catch (Exception ex)
            {
                // Preserve any created policy without auto-deleting it. This
                // allows an administrator to investigate partial commits and
                // prevents accidental deletion of other GPOs.
                throw new InvalidOperationException(
                    "Incomplete GPO deployment: " + ex.Message +
                    ". New GPO: " + (created?.ToString("D") ?? "(not created)") +
                    "; OU linked: " + linked +
                    "; campaign payload: " + targetPath +
                    ". Inspect before retry.", ex);
            }
            finally
            {
                if (!moved && Directory.Exists(stage))
                {
                    try { Directory.Delete(stage, recursive: true); }
                    catch (Exception cleanup) { AppLog.Write(cleanup, "GPO deploy stage cleanup"); }
                }
            }
        }

        if (!credentials.IsAlternate) return Run();
        string user = credentials.UserName.Trim();
        string? accountDomain = null;
        int slash = user.IndexOf('\\');
        if (slash > 0 && slash < user.Length - 1)
        {
            accountDomain = user[..slash];
            user = user[(slash + 1)..];
        }
        else if (!user.Contains('@')) user += "@" + credentials.Domain.Trim();
        if (string.IsNullOrEmpty(credentials.Password) ||
            !LogonUser(user, accountDomain, credentials.Password, 9, 3,
                out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot use alternate AD network credentials for GPO deployment.");
        using (token) return WindowsIdentity.RunImpersonated(token, Run);
    }

    private static void CopyValidatedPackage(string source, string destination)
    {
        if (!Path.IsPathFullyQualified(source) || !Directory.Exists(source))
            throw new DirectoryNotFoundException("Source campaign package is missing.");
        CopyAndVerify(Path.Combine(source, "campaign.json"),
            Path.Combine(destination, "campaign.json"), 1_048_576);
        using var cfg = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(source, "campaign.json")),
            new JsonDocumentOptions { MaxDepth = 12 });
        var clientType = NativeGpoDeploymentPlan.ParseClientType(
            cfg.RootElement.TryGetProperty("clientType", out var kind) &&
            kind.ValueKind == JsonValueKind.String ? kind.GetString() ?? "" : "");
        string agentName = clientType == CampaignClientType.Executable
            ? "LockScreen-Agent.exe" : "LockScreen-Agent.ps1";
        CopyAndVerify(Path.Combine(source, agentName),
            Path.Combine(destination, agentName),
            clientType == CampaignClientType.Executable ? 250L * 1_048_576 : 5 * 1_048_576);
        var slides = cfg.RootElement.GetProperty("slides");
        if (slides.ValueKind != JsonValueKind.Array || slides.GetArrayLength() is < 1 or > 30)
            throw new InvalidDataException("Source campaign slide count is invalid.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement slide in slides.EnumerateArray())
        {
            string leaf = slide.GetProperty("fileName").GetString() ?? "";
            if (!ImageName.IsMatch(leaf) || !seen.Add(leaf))
                throw new InvalidDataException("Source campaign image name is invalid.");
            string from = Path.Combine(source, "images", leaf);
            string to = Path.Combine(destination, "images", leaf);
            CopyAndVerify(from, to, 30 * 1_048_576);
            string expected = slide.GetProperty("sha256").GetString() ?? "";
            if (!Sha256(to).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Staged image SHA-256 mismatches manifest.");
        }
        // Recheck the source and copy as a matched pair before commit.
        foreach (var file in new[] { "campaign.json", agentName })
            if (!Sha256(Path.Combine(source, file)).Equals(
                    Sha256(Path.Combine(destination, file)), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Source package changed during staging.");
    }

    private static void CopyAndVerify(string source, string destination, long maxBytes)
    {
        if ((File.GetAttributes(source) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Refusing a non-regular package file.");
        var info = new FileInfo(source);
        if (info.Length < 1 || info.Length > maxBytes)
            throw new InvalidDataException("Source file exceeds its allowed size.");
        File.Copy(source, destination, overwrite: false);
        if (!Sha256(source).Equals(Sha256(destination), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Staged SYSVOL package copy SHA-256 mismatch.");
    }

    private static string Sha256(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Unsafe file type in staged SYSVOL package.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static void RestrictApplyToGroup(LdapConnection ldap, string gpoDn,
        SecurityIdentifier selectedSid)
    {
        var request = new SearchRequest(gpoDn, "(objectClass=groupPolicyContainer)",
            SearchScope.Base, "nTSecurityDescriptor");
        request.Controls.Add(new SecurityDescriptorFlagControl(SecurityMasks.Dacl));
        var response = (SearchResponse)ldap.SendRequest(request);
        if (response.Entries.Count != 1 ||
            response.Entries[0].Attributes["nTSecurityDescriptor"] is not { Count: > 0 } attr ||
            attr[0] is not byte[] binary)
            throw new InvalidOperationException("GPO DACL cannot be read.");
        var sd = new RawSecurityDescriptor(binary, 0);
        RawAcl? dacl = sd.DiscretionaryAcl;
        if (dacl is null)
            throw new InvalidOperationException("GPO has no explicit DACL.");
        const int ExtendedRight = 0x100;
        const int GenericRead = 0x00020094;
        string authenticated = "S-1-5-11";
        int removed = 0;
        for (int i = dacl.Count - 1; i >= 0; i--)
        {
            if (dacl[i] is not QualifiedAce ace ||
                !string.Equals(ace.SecurityIdentifier.Value, authenticated,
                    StringComparison.OrdinalIgnoreCase) ||
                ace.AceQualifier != AceQualifier.AccessAllowed)
                continue;
            bool apply = (ace.AccessMask & ExtendedRight) != 0 &&
                ace is ObjectAce objectAce &&
                (objectAce.ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent) != 0 &&
                objectAce.ObjectAceType == ApplyPolicy;
            // If Authenticated Users has a broad all-access ACE, the DACL
            // is not the standard new-GPO layout; never link it as-is.
            if ((ace.AccessMask & 0x10000000) != 0)
                throw new InvalidOperationException("Broad Authenticated Users rights prevent safe filtering.");
            if (apply && (ace.AceFlags & AceFlags.Inherited) == 0)
            {
                dacl.RemoveAce(i);
                removed++;
            }
            else if (apply)
                throw new InvalidOperationException("Inherited broad Apply Group Policy ACE remains.");
        }
        if (removed == 0)
            throw new InvalidOperationException("Cannot remove explicit Authenticated Users Apply ACE.");

        // Insert before inherited ACEs to preserve the ACL canonical order.
        int insert = 0;
        while (insert < dacl.Count && (dacl[insert].AceFlags & AceFlags.Inherited) == 0)
            insert++;
        dacl.InsertAce(insert++, new CommonAce(AceFlags.None,
            AceQualifier.AccessAllowed, GenericRead, selectedSid, false, null));
        dacl.InsertAce(insert, new ObjectAce(AceFlags.None,
            AceQualifier.AccessAllowed, ExtendedRight, selectedSid,
            ObjectAceFlags.ObjectAceTypePresent, ApplyPolicy,
            Guid.Empty, false, null));
        sd.DiscretionaryAcl = dacl;
        byte[] revised = new byte[sd.BinaryLength];
        sd.GetBinaryForm(revised, 0);
        var change = new ModifyRequest(gpoDn);
        var setAcl = new DirectoryAttributeModification
        {
            Name = "nTSecurityDescriptor", Operation = DirectoryAttributeOperation.Replace
        };
        setAcl.Add(revised);
        change.Modifications.Add(setAcl);
        change.Controls.Add(new SecurityDescriptorFlagControl(SecurityMasks.Dacl));
        ldap.SendRequest(change);

        var check = (SearchResponse)ldap.SendRequest(request);
        if (check.Entries.Count != 1 ||
            check.Entries[0].Attributes["nTSecurityDescriptor"] is not { Count: > 0 } readback ||
            readback[0] is not byte[] bytes)
            throw new IOException("GPO security filtering readback failed.");
        var verified = new RawSecurityDescriptor(bytes, 0);
        bool chosenApply = false;
        foreach (GenericAce generic in verified.DiscretionaryAcl!)
        {
            if (generic is not QualifiedAce ace ||
                ace.AceQualifier != AceQualifier.AccessAllowed ||
                ace is not ObjectAce objectAce ||
                (ace.AccessMask & ExtendedRight) == 0 ||
                (objectAce.ObjectAceFlags & ObjectAceFlags.ObjectAceTypePresent) == 0 ||
                objectAce.ObjectAceType != ApplyPolicy) continue;
            if (ace.SecurityIdentifier.Value == authenticated)
                throw new IOException("GPO still grants Apply to Authenticated Users.");
            if (ace.SecurityIdentifier == selectedSid ||
                ace.SecurityIdentifier.Value == selectedSid.Value)
                chosenApply = true;
        }
        if (!chosenApply)
            throw new IOException("GPO security group Apply ACE was not verified.");
    }

    private static void RegisterStartup(LdapConnection ldap, string gpoDn,
        string dc, string domain, Guid gpoId, string clientScript,
        CampaignClientType clientType)
    {
        string sysvol = @"\\" + dc + @"\SYSVOL\" + domain +
            @"\Policies\{" + gpoId.ToString("D").ToUpperInvariant() + "}";
        string gpt = Path.Combine(sysvol, "gpt.ini");
        if (!File.Exists(gpt))
            throw new FileNotFoundException("New GPO GPT is missing from selected DC.", gpt);
        string scripts = Path.Combine(sysvol, "Machine", "Scripts");
        Directory.CreateDirectory(scripts);
        // Independent GPO startup registrations:
        // PS1 uses PowerShell Startup Scripts (psscripts.ini).
        // EXE uses classic Startup Scripts (scripts.ini; direct EXE).
        string ini = Path.Combine(scripts, clientType == CampaignClientType.Executable
            ? "scripts.ini" : "psscripts.ini");
        if (File.Exists(ini) ||
            File.Exists(Path.Combine(scripts, clientType == CampaignClientType.Executable
                ? "psscripts.ini" : "scripts.ini")))
            throw new InvalidDataException("New GPO already has startup metadata.");
        string text;
        if (clientType == CampaignClientType.PowerShell)
        {
            text = "[ScriptsConfig]\r\nStartExecutePSFirst=true\r\n" +
                "[Startup]\r\n0CmdLine=" + clientScript +
                "\r\n0Parameters=-Mode Install\r\n";
        }
        else if (clientType == CampaignClientType.Executable)
        {
            // MS-GPSCR 2.2.2 permits a full UNC executable as 0CmdLine,
            // with 0Parameters passed to the executable. This uses NO
            // PowerShell and NO intermediate CMD/BAT launcher.
            if (!clientScript.EndsWith(@"\LockScreen-Agent.exe",
                    StringComparison.OrdinalIgnoreCase) ||
                clientScript.Length >= 260)
                throw new InvalidDataException("Native EXE startup path violates GPO limits.");
            text = "[Startup]\r\n0CmdLine=" + clientScript +
                "\r\n0Parameters=--client-agent Install\r\n";
        }
        else throw new NotSupportedException("Unsupported client type.");
        File.WriteAllText(ini, text, new UnicodeEncoding(false, true));
        var gptText = File.ReadAllText(gpt, Encoding.ASCII);
        if (!GptZero.IsMatch(gptText))
            throw new InvalidDataException("New GPT version was not zero.");
        File.WriteAllText(gpt, GptZero.Replace(gptText, "Version=1", 1),
            Encoding.ASCII);
        var update = new ModifyRequest(gpoDn);
        foreach (var (attribute, value) in new[]
        {
            ("versionNumber", "1"),
            ("gPCMachineExtensionNames", MachineExtension),
            ("flags", "1")
        })
        {
            var set = new DirectoryAttributeModification
            {
                Name = attribute, Operation = DirectoryAttributeOperation.Replace
            };
            set.Add(value);
            update.Modifications.Add(set);
        }
        ldap.SendRequest(update);
        var record = One(ldap, gpoDn, "(objectClass=groupPolicyContainer)",
            "versionNumber", "gPCMachineExtensionNames", "flags");
        if (NativeAdDiscovery.Attr(record, "versionNumber") != "1" ||
            NativeAdDiscovery.Attr(record, "gPCMachineExtensionNames") != MachineExtension ||
            NativeAdDiscovery.Attr(record, "flags") != "1" ||
            !File.Exists(ini))
            throw new IOException("GPO startup metadata was not verified.");
    }

    internal static void LinkNewGpo(LdapConnection ldap, string ouDn, string gpoDn)
    {
        var original = NativeAdDiscovery.Attr(
            One(ldap, ouDn, "(objectClass=organizationalUnit)", "gPLink"), "gPLink");
        if (original.Contains(gpoDn, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected GPO is already linked.");
        string updated = "[LDAP://" + gpoDn + ";0]" + original;
        var change = new ModifyRequest(ouDn);
        if (original.Length > 0)
        {
            var remove = new DirectoryAttributeModification
            {
                Name = "gPLink", Operation = DirectoryAttributeOperation.Delete
            };
            remove.Add(original);
            change.Modifications.Add(remove);
        }
        var add = new DirectoryAttributeModification
        {
            Name = "gPLink", Operation = DirectoryAttributeOperation.Add
        };
        add.Add(updated);
        change.Modifications.Add(add);
        ldap.SendRequest(change);
        if (!string.Equals(updated,
                NativeAdDiscovery.Attr(One(ldap, ouDn,
                    "(objectClass=organizationalUnit)", "gPLink"), "gPLink"),
                StringComparison.Ordinal))
            throw new IOException("New OU GPO link could not be verified.");
    }

    private static void ValidateLabCanaryGroup(LdapConnection ldap,
        string groupDn, string targetOuDn, int expectedDirectMembers)
    {
        // This temporary, explicitly opted-in deployment method is only
        // suitable for a few directly listed test computers. Reject any
        // nested group, out-of-OU computer, or size drift. This is not proof
        // of the full effective token, so it never enables production mode.
        if (expectedDirectMembers is < 0 or > 5)
            throw new InvalidOperationException(
                "EXE lab canary accepts no more than 5 directly assigned computers.");
        var group = One(ldap, groupDn, "(objectClass=group)",
            "sAMAccountName", "member");
        string sam = NativeAdDiscovery.Attr(group, "sAMAccountName");
        if (sam.Equals("Domain Computers", StringComparison.OrdinalIgnoreCase) ||
            sam.Equals("Authenticated Users", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "EXE lab canary refuses broad built-in computer groups.");
        int actual = NativeGpoDeploymentPlan.CountDirectMembers(group);
        if (actual != expectedDirectMembers)
            throw new InvalidOperationException(
                "Lab canary group membership changed after Preview.");
        if (!group.Attributes.Contains("member")) return;
        foreach (object value in group.Attributes["member"]!)
        {
            string memberDn = Convert.ToString(value,
                CultureInfo.InvariantCulture) ?? "";
            if (!memberDn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ||
                memberDn.Length > 2048 ||
                !NativeGpoConflictScan.Inside(memberDn, targetOuDn))
                throw new InvalidOperationException(
                    "EXE lab canary group contains an out-of-scope object.");
            _ = One(ldap, memberDn, "(objectClass=computer)",
                "distinguishedName");
        }
    }

    private static SearchResultEntry One(LdapConnection ldap,
        string dn, string filter, params string[] fields)
    {
        var result = (SearchResponse)ldap.SendRequest(
            new SearchRequest(dn, filter, SearchScope.Base, fields));
        if (result.Entries.Count != 1)
            throw new InvalidDataException("Required AD record not available on selected DC.");
        return result.Entries[0];
    }

    private static string Get(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static string Required(JsonElement obj, string key)
    {
        string value = Get(obj, key);
        return value.Length == 0 ? throw new InvalidDataException("Missing verified GPO plan field: " + key)
            : value;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string username, string? domain,
        string password, int logonType, int provider,
        out SafeAccessTokenHandle token);
}
