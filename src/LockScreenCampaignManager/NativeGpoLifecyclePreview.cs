using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace LockScreenCampaignManager;

/// <summary>
/// Native Preview and guarded Apply for managed LockScreen GPO lifecycle
/// actions. Apply rebuilds the SAME plan digest from fresh LDAP and SYSVOL
/// before a scoped link change or COM deletion. Reuse is not yet native.
/// </summary>
internal static class NativeGpoLifecyclePreview
{
    private const int MaxLinks = 12000;
    private const int MaxContainers = 10000;
    private static readonly TimeSpan AuditTimeout = TimeSpan.FromMinutes(4);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Regex GptVersionPattern = new(
        @"(?im)^Version=(?<version>\d+)\s*$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly HashSet<string> Actions =
        ["Disable", "Enable", "Unlink", "Relink", "Delete", "Reuse"];

    internal sealed record Link(string ou, int flags, bool enabled, bool enforced,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? type);
    private sealed record GpoState(string Dn, string Name, long Version, int Flags);
    private sealed record ReusePackage(string CampaignId, string Hash);

    private sealed record PreviewData(
        string action, string name, string gpoId, string campaignId,
        string newCampaignId, string domain, string dc, string ou,
        bool linked, bool linkEnabled, IReadOnlyList<Link> links,
        string hash, long version, string note);

    internal static JsonElement Execute(string mode, object request, DomainCredentials credentials)
    {
        if (mode is not ("Preview" or "Apply"))
            throw new ArgumentException("Unsupported native lifecycle mode.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("LDAP and SYSVOL require Windows.");

        JsonElement args = JsonSerializer.SerializeToElement(request, Json);
        string domain = Get(args, "domain").Trim().ToLowerInvariant();
        string requestedDc = Get(args, "dc").Trim();
        NativeAdDiscovery.ValidateNames(domain, requestedDc);
        if (requestedDc.Length == 0)
            throw new ArgumentException("Select a domain controller before preparing a GPO plan.");

        string ouDn = Get(args, "ouDn");
        string requestedId = Get(args, "gpoId");
        string action = Get(args, "action");
        string packagePath = Get(args, "packagePath");
        if (!Actions.Contains(action) ||
            !Guid.TryParse(requestedId, out Guid gpoId) || gpoId == Guid.Empty)
            throw new ArgumentException("Choose a supported action and a valid GPO GUID.");

        JsonElement Run()
        {
            using var ldap = NativeAdDiscovery.Connect(requestedDc, credentials);
            ldap.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            var root = NativeAdDiscovery.ReadRootDse(ldap);
            string baseDn = Required(root, "defaultNamingContext");
            string actualDc = Required(root, "dnsHostName");
            string configDn = Required(root, "configurationNamingContext");
            string clock = Required(root, "currentTime");
            string legacyDomain = DomainFromDnPreserveCase(baseDn);
            if (!legacyDomain.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                !NativeGpoLifecycleList.SameController(requestedDc, actualDc))
                throw new InvalidOperationException(
                    "The connected domain controller does not match the selected AD domain/DC.");
            if (NativeAdDiscovery.ReadDcWritable(ldap, root) != true)
                throw new InvalidOperationException("Writable AD controller cannot be verified.");
            DateTime dcUtc = NativeDcConsistency.ParseDcTime(clock);
            int skewSeconds = (int)Math.Round((DateTime.UtcNow - dcUtc).TotalSeconds);
            var timer = Stopwatch.StartNew();

            if (!ouDn.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ||
                !NativeGpoConflictScan.Inside(ouDn, baseDn) ||
                ouDn.Length > 2048 || ouDn.Any(char.IsControl) ||
                !string.Equals(ouDn.Trim(), ouDn, StringComparison.Ordinal))
                throw new InvalidDataException("Selected OU is invalid or outside the connected domain.");

            _ = ReadOne(ldap, ouDn, "(objectClass=organizationalUnit)",
                "distinguishedName");
            string gpoDn = NativeGpoLifecycleList.PolicyDn(gpoId, baseDn);
            var gpoEntry = ReadOne(ldap, gpoDn,
                "(objectClass=groupPolicyContainer)",
                "displayName", "versionNumber", "flags");
            long version = LongAttr(gpoEntry, "versionNumber");
            int flags = checked((int)LongAttr(gpoEntry, "flags", optional: true));
            if (version < 0 || version > uint.MaxValue || flags is < 0 or > 3)
                throw new InvalidDataException("Invalid AD GPO version or flags.");
            string display = NativeAdDiscovery.Attr(gpoEntry, "displayName");
            var gpo = new GpoState(gpoDn, display, version, flags);

            var managed = NativeGpoLifecycleList.ReadManagedGpo(actualDc, domain, gpoId)
                ?? throw new InvalidOperationException(
                    "Selected policy is not a managed LockScreen campaign. Refusing a preview.");

            string gpt = @"\\" + actualDc + @"\SYSVOL\" + domain +
                @"\Policies\{" + gpoId.ToString("D").ToUpperInvariant() + "}";
            string psScriptFile = Path.Combine(gpt, "Machine", "Scripts", "psscripts.ini");
            string classicScriptFile = Path.Combine(gpt, "Machine", "Scripts", "scripts.ini");
            bool nativeClient = !File.Exists(psScriptFile) && File.Exists(classicScriptFile);
            string scriptFile = nativeClient ? classicScriptFile : psScriptFile;
            if (action == "Reuse" && nativeClient)
                throw new NotSupportedException(
                    "EXE GPO Reuse requires a separate method-specific migration. " +
                    "Create a fresh EXE GPO instead.");
            string iniHash = FileSha256(scriptFile, 1, 1_048_576);
            string gptText = ReadBoundedText(Path.Combine(gpt, "gpt.ini"),
                1, 1_048_576, Encoding.UTF8);
            var m = GptVersionPattern.Match(gptText);
            if (!m.Success ||
                !long.TryParse(m.Groups["version"].Value,
                    NumberStyles.None, CultureInfo.InvariantCulture, out long gptVersion) ||
                gptVersion != gpo.Version)
                throw new InvalidOperationException(
                    "GPO version mismatch between AD and SYSVOL. Refresh before applying.");

            string rawLinks = NativeAdDiscovery.Attr(ReadOne(ldap, ouDn,
                "(objectClass=organizationalUnit)", "gPLink"), "gPLink");
            var linkEntries = NativeGpoLifecycleList.ParseLinks(rawLinks)
                .Where(x => x.Dn.Equals(gpoDn, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (linkEntries.Length > 1)
                throw new InvalidOperationException("Duplicate GPO links detected in selected OU.");
            var selectedLink = linkEntries.FirstOrDefault();
            var allLinks = CollectLinks(ldap, actualDc, baseDn, configDn, gpoDn, timer);

            CheckAction(action, selectedLink is not null, allLinks.Count);
            string newCampaignId = "";
            string packageHash = "";
            if (action == "Reuse")
            {
                if (Math.Abs((long)skewSeconds) > 300)
                    throw new InvalidOperationException("Computer and selected DC clocks differ by more than five minutes.");
                var package = ValidateReusePackage(packagePath, dcUtc);
                newCampaignId = package.CampaignId;
                packageHash = package.Hash;
                if (newCampaignId.Equals(managed.campaignId,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Reuse requires a new campaign ID.");

                string oldManifest = @"\\" + actualDc + @"\SYSVOL\" + domain +
                    @"\scripts\LockScreenCampaigns\" + managed.campaignId + @"\campaign.json";
                using var old = JsonDocument.Parse(ReadBoundedBytes(oldManifest, 10, 1_048_576),
                    new JsonDocumentOptions { MaxDepth = 12 });
                string endText = RequiredString(old.RootElement, "endLocal");
                string zoneId = RequiredString(old.RootElement, "timeZoneId");
                DateTime endLocal = ParseLocal(endText);
                TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                if (TimeZoneInfo.ConvertTimeFromUtc(dcUtc, zone) < endLocal)
                    throw new InvalidOperationException("The existing campaign has not yet ended.");

                string newSource = @"\\" + actualDc + @"\SYSVOL\" + domain +
                    @"\scripts\LockScreenCampaigns\" + newCampaignId;
                if (ExistsStrict(newSource))
                    throw new InvalidOperationException("The new campaign is already present in SYSVOL.");
            }

            string planHash = LegacyPlanHash(legacyDomain, actualDc, gpo.Dn, ouDn,
                rawLinks, gpo.Version, gpo.Flags, iniHash, action,
                newCampaignId, packageHash);
            var preview = new PreviewData(
                action, gpo.Name, gpoId.ToString("D"), managed.campaignId,
                newCampaignId, legacyDomain, actualDc, ouDn,
                selectedLink is not null,
                selectedLink is not null && (selectedLink.Flags & 1) == 0,
                allLinks, planHash, gptVersion, Note(action));
            if (mode == "Apply")
            {
                // Reuse and Delete require independent, fresh state checks
                // beyond GUID/plan-hash equality; link operations remain atomic.
                string approval = Get(args, "approveGuid").Trim();
                string approvedHash = Get(args, "planHash");
                if (!Guid.TryParse(approval, out Guid approvedId) ||
                    approvedId != gpoId ||
                    !string.Equals(approvedHash, planHash, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Confirmation/preview mismatch. Refresh and confirm the exact GPO GUID.");

                if (action == "Delete")
                {
                    // Recheck links against the selected DC immediately prior
                    // to the irreversible COM deletion. The initial preview
                    // check also covered both OU/domain and site gPLink.
                    var linksNow = CollectLinks(ldap, actualDc, baseDn, configDn,
                        gpoDn, Stopwatch.StartNew());
                    if (allLinks.Count != 0 || linksNow.Count != 0)
                        throw new InvalidOperationException(
                            "The selected GPO is linked. Unlink it from every OU, domain and site before deleting.");
                    var current = ReadOne(ldap, gpoDn,
                        "(objectClass=groupPolicyContainer)",
                        "versionNumber", "flags");
                    if (LongAttr(current, "versionNumber") != gpo.Version ||
                        LongAttr(current, "flags", optional: true) != gpo.Flags)
                        throw new InvalidOperationException(
                            "GPO changed since confirmation. Refresh Preview.");

                    NativeGpoCom.DeleteManagedUnlinkedGpo(actualDc, gpoDn);
                    return JsonSerializer.SerializeToElement(new
                    {
                        success = true,
                        message = "Selected unlinked managed GPO deleted using native Windows GPO COM. Review AD replication.",
                        data = new
                        {
                            action, gpoId = gpoId.ToString("D"),
                            campaignId = managed.campaignId, ou = ouDn,
                            successful = true, note = Note(action),
                            oldSourcePreserved = true
                        }
                    }, Json);
                }

                if (action == "Reuse")
                {
                    long newVersion = ApplyReuse(ldap, actualDc, domain, gpoId,
                        gpo, packagePath, newCampaignId, dcUtc);
                    return JsonSerializer.SerializeToElement(new
                    {
                        success = true,
                        message = "Managed GPO reused for a new campaign on the selected DC. Verify SYSVOL and AD replication.",
                        data = new
                        {
                            action, gpoId = gpoId.ToString("D"),
                            campaignId = managed.campaignId, ou = ouDn,
                            successful = true, note = Note(action),
                            oldSourcePreserved = true, newCampaignId, newVersion
                        }
                    }, Json);
                }

                // LDAP atomic modify: delete the exact original single-value
                // gPLink and add the replacement, or add/delete when empty.
                // If an administrator changes gPLink after our read, the
                // original-value delete fails instead of overwriting their edit.
                string oldSegment = selectedLink is null ? "" :
                    "[LDAP://" + selectedLink.Dn + ";" +
                    selectedLink.Flags.ToString(CultureInfo.InvariantCulture) + "]";
                string newSegment = action switch
                {
                    "Disable" => "[LDAP://" + selectedLink!.Dn + ";" +
                        (selectedLink.Flags | 1).ToString(CultureInfo.InvariantCulture) + "]",
                    "Enable" => "[LDAP://" + selectedLink!.Dn + ";" +
                        (selectedLink.Flags & ~1).ToString(CultureInfo.InvariantCulture) + "]",
                    "Relink" => "[LDAP://" + gpo.Dn + ";0]",
                    _ => ""
                };
                string updatedLinks;
                if (action == "Relink")
                    updatedLinks = newSegment + rawLinks;
                else
                {
                    var exact = Regex.Matches(rawLinks, Regex.Escape(oldSegment),
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        TimeSpan.FromSeconds(1));
                    if (exact.Count != 1)
                        throw new InvalidOperationException("Selected link is ambiguous. Refresh the preview.");
                    updatedLinks = rawLinks.Remove(exact[0].Index, exact[0].Length)
                        .Insert(exact[0].Index, newSegment);
                }
                if (string.Equals(updatedLinks, rawLinks, StringComparison.Ordinal))
                    throw new InvalidOperationException("Link already has the requested state.");

                var change = new ModifyRequest(ouDn);
                if (rawLinks.Length > 0)
                {
                    var deleteOriginal = new DirectoryAttributeModification
                    {
                        Name = "gPLink", Operation = DirectoryAttributeOperation.Delete
                    };
                    deleteOriginal.Add(rawLinks);
                    change.Modifications.Add(deleteOriginal);
                }
                if (updatedLinks.Length > 0)
                {
                    var addUpdated = new DirectoryAttributeModification
                    {
                        Name = "gPLink", Operation = DirectoryAttributeOperation.Add
                    };
                    addUpdated.Add(updatedLinks);
                    change.Modifications.Add(addUpdated);
                }
                ldap.SendRequest(change);
                string actualLinks = NativeAdDiscovery.Attr(
                    ReadOne(ldap, ouDn, "(objectClass=organizationalUnit)", "gPLink"),
                    "gPLink");
                if (!string.Equals(actualLinks, updatedLinks, StringComparison.Ordinal))
                    throw new IOException("OU link update could not be verified. Inspect AD before retrying.");
                return JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    message = "Selected OU GPO link updated and verified on the selected DC.",
                    data = new
                    {
                        action, gpoId = gpoId.ToString("D"),
                        campaignId = managed.campaignId, ou = ouDn,
                        successful = true, note = Note(action),
                        oldSourcePreserved = true
                    }
                }, Json);
            }
            return JsonSerializer.SerializeToElement(new
            {
                success = true,
                message = "Native read-only GPO lifecycle preview. No changes made.",
                data = preview
            }, Json);
        }

        if (!credentials.IsAlternate) return Run();
        string username = credentials.UserName.Trim();
        string? accountDomain = null;
        int slash = username.IndexOf('\\');
        if (slash > 0 && slash < username.Length - 1)
        {
            accountDomain = username[..slash];
            username = username[(slash + 1)..];
        }
        else if (!username.Contains('@'))
            username += "@" + credentials.Domain.Trim();
        if (credentials.Password.Length == 0 ||
            !LogonUser(username, accountDomain, credentials.Password, 9, 3,
                out SafeAccessTokenHandle token))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Cannot establish alternate network credentials for a GPO lifecycle preview.");
        using (token)
            return WindowsIdentity.RunImpersonated(token, Run);
    }

    internal static string DomainFromDnPreserveCase(string baseDn)
    {
        // Mirrors the legacy PowerShell DomainInfo regex exactly. A DNS
        // comparison ignores case; the SHA-256 plan preimage MUST NOT.
        var parts = Regex.Matches(baseDn,
            @"(?i)(?:^|,)DC=([^,]+)", RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (parts.Count < 2)
            throw new InvalidDataException("Unexpected AD domain naming context.");
        return string.Join(".", parts.Select(x => x.Groups[1].Value));
    }

    internal static string LegacyPlanHash(string domain, string dc,
        string gpoDn, string ouDn, string rawGpoLink, long version, int flags,
        string psscriptsHash, string action, string newCampaignId,
        string packageHash)
    {
        if (!Actions.Contains(action) ||
            !Regex.IsMatch(psscriptsHash, @"\A[0-9a-fA-F]{64}\z") ||
            (packageHash.Length > 0 &&
             !Regex.IsMatch(packageHash, @"\A[0-9a-fA-F]{64}:[0-9a-fA-F]{64}\z")))
            throw new InvalidDataException("Invalid GPO lifecycle preview hash components.");
        // PowerShell 5.1 Get-FileHash.Hash returns UPPERCASE hex. Crucially,
        // do not normalize the AD DN, selected OU, gPLink or DC spelling.
        // The guarded Apply recomputes exactly this UTF-8 preimage.
        string source = string.Join("|", new[] {
            domain, dc, gpoDn, ouDn, rawGpoLink,
            version.ToString(CultureInfo.InvariantCulture),
            flags.ToString(CultureInfo.InvariantCulture),
            psscriptsHash.ToUpperInvariant(), action, newCampaignId,
            packageHash.ToUpperInvariant()
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    internal static void CheckAction(string action, bool linked, int linkCount)
    {
        if (!Actions.Contains(action))
            throw new ArgumentException("Unsupported GPO lifecycle action.");
        if ((action is "Disable" or "Enable" or "Unlink") && !linked)
            throw new InvalidOperationException("GPO is not linked to selected OU.");
        if (action == "Relink" && linked)
            throw new InvalidOperationException("GPO is already linked to selected OU.");
        if (action == "Delete" && linkCount > 0)
            throw new InvalidOperationException("Unlink the GPO from all OUs and sites before deletion.");
    }

    private static string Note(string action) => action switch
    {
        "Disable" => "Disables ONLY the chosen OU link. Already installed local tasks CONTINUE until separately cleaned up.",
        "Enable" => "Re-enables ONLY the chosen OU link. Expired campaigns do not restart. Use Reuse to replace the manifest.",
        "Unlink" => "Removes ONLY the selected OU link. GPO and SYSVOL content stay available.",
        "Relink" => "Recreates an enabled link to the selected OU. Existing GPO and security filtering stay unchanged.",
        "Delete" => "Permanently deletes the UNLINKED GPO. The campaign source folder stays in SYSVOL for separate manual review.",
        "Reuse" => "Updates SAME GPO with a NEW campaign manifest/script. Keeps its GUID, link states and security filtering unchanged.",
        _ => throw new ArgumentException("Unsupported GPO lifecycle action.")
    };

    private static string Get(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(name, out JsonElement item) &&
        item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";

    private static string Required(SearchResultEntry entry, string name)
    {
        string value = NativeAdDiscovery.Attr(entry, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("The selected DC did not return required GPO metadata.");
        return value;
    }

    private static SearchResultEntry ReadOne(LdapConnection ldap, string dn,
        string filter, params string[] attributes)
    {
        var result = (SearchResponse)ldap.SendRequest(
            new SearchRequest(dn, filter, SearchScope.Base, attributes));
        if (result.Entries.Count != 1)
            throw new InvalidDataException("GPO or OU record is missing on the selected DC.");
        return result.Entries[0];
    }

    private static long LongAttr(SearchResultEntry entry, string name, bool optional = false)
    {
        string raw = NativeAdDiscovery.Attr(entry, name);
        if (optional && raw.Length == 0) return 0;
        if (!long.TryParse(raw, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long value))
            throw new InvalidDataException("GPO numeric property is malformed.");
        return value;
    }

    private static List<Link> CollectLinks(LdapConnection ldap, string actualDc,
        string baseDn, string configDn, string targetGpoDn, Stopwatch timer)
    {
        var links = new List<Link>();
        foreach (var (rootDn, filter, site) in new[] {
            (baseDn, "(|(objectClass=organizationalUnit)(objectClass=domainDNS))", false),
            ("CN=Sites," + configDn, "(objectClass=site)", true)
        })
        {
            foreach (SearchResultEntry entry in NativeGpoLifecycleList.SearchPaged(
                         ldap, rootDn, filter, 10000, timer, "gPLink"))
            {
                string raw = NativeAdDiscovery.Attr(entry, "gPLink");
                if (raw.Length == 0) continue;
                foreach (var link in NativeGpoLifecycleList.ParseLinks(raw))
                {
                    if (links.Count >= MaxLinks)
                        throw new InvalidDataException("GPO link inventory exceeds safe limits.");
                    if (!link.Dn.Equals(targetGpoDn, StringComparison.OrdinalIgnoreCase)) continue;
                    links.Add(new Link(site
                        ? "LDAP://" + actualDc + "/" + entry.DistinguishedName
                        : entry.DistinguishedName,
                        link.Flags, (link.Flags & 1) == 0,
                        (link.Flags & 2) != 0, site ? "Site" : null));
                }
            }
        }
        return links;
    }

    // Reuse preserves the original GPO GUID, all existing OU links and
    // security filtering. The new package is staged and SHA-256 verified
    // before touching psscripts.ini or either GPO version. Partial failures
    // attempt a bounded rollback; the new source package is preserved for
    // forensic review, matching the legacy PS1 behavior.
    private static long ApplyReuse(LdapConnection ldap, string dc, string domain,
        Guid gpoId, GpoState gpo, string packageFolder, string newId, DateTime dcUtc)
    {
        var validated = ValidateReusePackage(packageFolder, dcUtc);
        if (!string.Equals(validated.CampaignId, newId, StringComparison.Ordinal))
            throw new InvalidOperationException("Campaign changed since preview. Rebuild the plan.");
        if (gpo.Version is < 0 or > uint.MaxValue ||
            (gpo.Version & 0xFFFF) == 0xFFFF)
            throw new InvalidOperationException("GPO computer version overflow.");

        string sourceRoot = @"\\" + dc + @"\SYSVOL\" + domain +
            @"\scripts\LockScreenCampaigns";
        SafeDirectory(sourceRoot);
        string finalDir = Path.Combine(sourceRoot, newId);
        if (ExistsStrict(finalDir))
            throw new InvalidOperationException("New campaign ID already exists in SYSVOL.");
        string tempDir = Path.Combine(sourceRoot, ".lscm-stage-" +
            Guid.NewGuid().ToString("N"));
        string gptRoot = @"\\" + dc + @"\SYSVOL\" + domain +
            @"\Policies\{" + gpoId.ToString("D").ToUpperInvariant() + "}";
        string ini = Path.Combine(gptRoot, "Machine", "Scripts", "psscripts.ini");
        string gpt = Path.Combine(gptRoot, "gpt.ini");
        byte[] oldIni = ReadBoundedBytes(ini, 1, 1_048_576);
        byte[] oldGpt = ReadBoundedBytes(gpt, 1, 1_048_576);
        string oldGptText = Encoding.UTF8.GetString(oldGpt);
        var versionMatch = GptVersionPattern.Match(oldGptText);
        if (!versionMatch.Success ||
            !long.TryParse(versionMatch.Groups["version"].Value,
                NumberStyles.None, CultureInfo.InvariantCulture, out long diskVersion) ||
            diskVersion != gpo.Version)
            throw new InvalidOperationException("GPO AD/SYSVOL version changed. Refresh Preview.");

        long newVersion = (gpo.Version & ~65535L) | ((gpo.Version + 1) & 65535L);
        string clientScript = @"\\" + domain + @"\SYSVOL\" + domain +
            @"\scripts\LockScreenCampaigns\" + newId + @"\LockScreen-Agent.ps1";
        if (clientScript.Length >= 260)
            throw new InvalidDataException("New client script path exceeds the Windows limit.");
        string iniText = "[ScriptsConfig]\r\nStartExecutePSFirst=true\r\n" +
            "[Startup]\r\n0CmdLine=" + clientScript +
            "\r\n0Parameters=-Mode Install\r\n";
        string updatedGpt = GptVersionPattern.Replace(oldGptText,
            "Version=" + newVersion.ToString(CultureInfo.InvariantCulture), 1);
        bool iniWritten = false, gptWritten = false, adWritten = false;
        bool moved = false;

        try
        {
            // Staging is always within the selected DC's SYSVOL share.
            Directory.CreateDirectory(tempDir);
            string imagesOut = Path.Combine(tempDir, "images");
            Directory.CreateDirectory(imagesOut);
            foreach (string file in new[] { "campaign.json", "LockScreen-Agent.ps1" })
            {
                string input = Path.Combine(packageFolder, file);
                string output = Path.Combine(tempDir, file);
                File.Copy(input, output, overwrite: false);
                if (!string.Equals(FileSha256(input, 1, 5 * 1_048_576),
                        FileSha256(output, 1, 5 * 1_048_576),
                        StringComparison.OrdinalIgnoreCase))
                    throw new IOException("SYSVOL staged campaign file hash mismatch.");
            }
            using (var manifest = JsonDocument.Parse(
                ReadBoundedBytes(Path.Combine(packageFolder, "campaign.json"),
                    10, 1_048_576)))
            {
                foreach (JsonElement slide in manifest.RootElement.GetProperty("slides").EnumerateArray())
                {
                    string image = RequiredString(slide, "fileName");
                    if (!Regex.IsMatch(image, @"\A[0-9]{2}\.jpg\z"))
                        throw new InvalidDataException("Invalid image path in campaign manifest.");
                    string input = Path.Combine(packageFolder, "images", image);
                    string output = Path.Combine(imagesOut, image);
                    File.Copy(input, output, overwrite: false);
                    if (!string.Equals(FileSha256(input, 1, 30 * 1_048_576),
                            FileSha256(output, 1, 30 * 1_048_576),
                            StringComparison.OrdinalIgnoreCase))
                        throw new IOException("SYSVOL staged image SHA-256 mismatch.");
                }
            }
            // Revalidation after staging prevents package changes during copy.
            var fresh = ValidateReusePackage(packageFolder, dcUtc);
            if (!string.Equals(fresh.Hash, validated.Hash, StringComparison.OrdinalIgnoreCase) ||
                ExistsStrict(finalDir))
                throw new IOException("Campaign package or SYSVOL target changed during staging.");
            Directory.Move(tempDir, finalDir);
            moved = true;

            // Check AD version immediately before the first GPO modification.
            if (LongAttr(ReadOne(ldap, gpo.Dn, "(objectClass=groupPolicyContainer)",
                "versionNumber"), "versionNumber") != gpo.Version)
                throw new InvalidOperationException("GPO changed during staging. Refresh Preview.");

            File.WriteAllText(ini, iniText, new UnicodeEncoding(false, true));
            iniWritten = true;
            File.WriteAllText(gpt, updatedGpt, Encoding.ASCII);
            gptWritten = true;

            var update = new ModifyRequest(gpo.Dn);
            var deleteOld = new DirectoryAttributeModification
            {
                Name = "versionNumber", Operation = DirectoryAttributeOperation.Delete
            };
            deleteOld.Add(gpo.Version.ToString(CultureInfo.InvariantCulture));
            update.Modifications.Add(deleteOld);
            var addNew = new DirectoryAttributeModification
            {
                Name = "versionNumber", Operation = DirectoryAttributeOperation.Add
            };
            addNew.Add(newVersion.ToString(CultureInfo.InvariantCulture));
            update.Modifications.Add(addNew);
            ldap.SendRequest(update);
            adWritten = true;

            var managedNew = NativeGpoLifecycleList.ReadManagedGpo(dc, domain, gpoId);
            if (managedNew is null ||
                !string.Equals(managedNew.campaignId, newId, StringComparison.Ordinal))
                throw new IOException("GPO Startup reference to new campaign was not verified.");
            if (LongAttr(ReadOne(ldap, gpo.Dn, "(objectClass=groupPolicyContainer)",
                    "versionNumber"), "versionNumber") != newVersion)
                throw new IOException("New GPO version was not verified on the selected DC.");
            return newVersion;
        }
        catch (Exception ex)
        {
            // Restore only files we actually modified, and never blindly
            // restore AD when another actor changed the GPO since our write.
            try { if (iniWritten) File.WriteAllBytes(ini, oldIni); }
            catch (Exception rollback) { AppLog.Write(rollback, "GPO Reuse rollback ini"); }
            try { if (gptWritten) File.WriteAllBytes(gpt, oldGpt); }
            catch (Exception rollback) { AppLog.Write(rollback, "GPO Reuse rollback gpt.ini"); }
            if (adWritten)
            {
                try
                {
                    long current = LongAttr(ReadOne(ldap, gpo.Dn,
                        "(objectClass=groupPolicyContainer)", "versionNumber"), "versionNumber");
                    if (current == newVersion)
                    {
                        var revert = new ModifyRequest(gpo.Dn,
                            DirectoryAttributeOperation.Replace, "versionNumber",
                            gpo.Version.ToString(CultureInfo.InvariantCulture));
                        ldap.SendRequest(revert);
                    }
                }
                catch (Exception rollback) { AppLog.Write(rollback, "GPO Reuse rollback AD"); }
            }
            throw new InvalidOperationException(
                "GPO Reuse failed; rollback attempted. Verify AD/SYSVOL replication manually. " +
                ex.Message, ex);
        }
        finally
        {
            // Preserve the moved source package when a partial GPO write
            // requires administrative forensic review, just as legacy PS1.
            if (!moved && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch (Exception cleanup) { AppLog.Write(cleanup, "GPO Reuse stage cleanup"); }
            }
        }
    }

    private static ReusePackage ValidateReusePackage(string packageFolder, DateTime dcUtc)
    {
        if (string.IsNullOrWhiteSpace(packageFolder) ||
            packageFolder.Length > 2000 || !Path.IsPathFullyQualified(packageFolder))
            throw new InvalidDataException("Export the campaign before reusing the GPO.");
        SafeDirectory(packageFolder);
        string manifest = Path.Combine(packageFolder, "campaign.json");
        string agent = Path.Combine(packageFolder, "LockScreen-Agent.ps1");
        string manifestHash = FileSha256(manifest, 10, 1_048_576);
        string agentHash = FileSha256(agent, 1, 5 * 1_048_576);
        using var doc = JsonDocument.Parse(ReadBoundedBytes(manifest, 10, 1_048_576),
            new JsonDocumentOptions { MaxDepth = 12 });
        JsonElement cfg = doc.RootElement;
        string campaignId = RequiredString(cfg, "campaignId");
        if (!NativeSysvolStorage.ValidCampaignId(campaignId) ||
            cfg.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Invalid exported campaign manifest.");
        string timeZoneId = RequiredString(cfg, "timeZoneId");
        TimeZoneInfo timezone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        DateTime end = ParseLocal(RequiredString(cfg, "endLocal"));
        if (timezone.IsAmbiguousTime(end) || timezone.IsInvalidTime(end) ||
            TimeZoneInfo.ConvertTimeFromUtc(dcUtc, timezone) >= end)
            throw new InvalidOperationException("New campaign has ended or has an ambiguous end time.");
        if (cfg.TryGetProperty("agentSha256", out JsonElement expectedAgent) &&
            !string.Equals(expectedAgent.GetString(), agentHash,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Exported campaign agent SHA-256 mismatches manifest.");
        var slides = cfg.GetProperty("slides");
        if (slides.ValueKind != JsonValueKind.Array || slides.GetArrayLength() is < 1 or > 30)
            throw new InvalidDataException("Expected 1-30 campaign images.");
        SafeDirectory(Path.Combine(packageFolder, "images"));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateTime previous = DateTime.MinValue;
        foreach (JsonElement slide in slides.EnumerateArray())
        {
            string file = RequiredString(slide, "fileName");
            string expected = RequiredString(slide, "sha256");
            DateTime at = ParseLocal(RequiredString(slide, "atLocal"));
            if (!Regex.IsMatch(file, @"\A[0-9]{2}\.jpg\z") ||
                !seen.Add(file) ||
                !Regex.IsMatch(expected, @"\A[0-9a-fA-F]{64}\z") ||
                at <= previous || at >= end ||
                timezone.IsAmbiguousTime(at) || timezone.IsInvalidTime(at))
                throw new InvalidDataException("Campaign image or slide schedule is invalid.");
            previous = at;
            string actual = FileSha256(Path.Combine(packageFolder, "images", file),
                1, 30 * 1_048_576);
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Campaign image SHA-256 mismatches manifest.");
        }
        return new ReusePackage(campaignId, manifestHash + ":" + agentHash);
    }

    private static string RequiredString(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object &&
        json.TryGetProperty(name, out var item) &&
        item.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(item.GetString()) ?
        item.GetString()! : throw new InvalidDataException(
            "Required campaign manifest data is missing.");

    private static DateTime ParseLocal(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-ddTHH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static void SafeDirectory(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Campaign folder is not a regular directory.");
    }

    private static bool ExistsStrict(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static byte[] ReadBoundedBytes(string path, long min, long max)
    {
        FileAttributes attrs = File.GetAttributes(path);
        if ((attrs & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Referenced GPO file is unsafe.");
        var info = new FileInfo(path);
        if (info.Length < min || info.Length > max)
            throw new InvalidDataException("Referenced GPO file has an unexpected size.");
        return File.ReadAllBytes(path);
    }

    private static string ReadBoundedText(string path, long min, long max,
        Encoding encoding) => encoding.GetString(ReadBoundedBytes(path, min, max));

    private static string FileSha256(string path, long min, long max)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Referenced GPO file is not regular.");
        var info = new FileInfo(path);
        if (info.Length < min || info.Length > max)
            throw new InvalidDataException("Referenced GPO file is missing or oversized.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string username, string? domain,
        string password, int logonType, int provider, out SafeAccessTokenHandle token);
}
