using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only storage evidence displayed by the AD/GPO deployment wizard.
/// Advisory ACL scanning never establishes a client's effective SMB access.
/// </summary>
internal static class CampaignSmbPreflightEvidence
{
    private static string Required(JsonElement data, string key)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty(key, out JsonElement item) ||
            item.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(item.GetString()))
            throw new InvalidDataException("Incomplete storage preflight evidence: " + key);
        return item.GetString()!;
    }

    internal static string Describe(JsonElement plan)
    {
        string mode = Required(plan, "contentSource");
        if (mode == "Sysvol")
        {
            if (Required(plan, "smbNtfsAclScan") != "NOT_APPLICABLE" ||
                Required(plan, "smbComputerAccountRead") != "NOT_APPLICABLE")
                throw new InvalidDataException("Unexpected SMB evidence in SYSVOL preflight.");
            return "SYSVOL mode: no external SMB content-source permissions to assess.\r\n";
        }
        if (mode != "SmbShare")
            throw new InvalidDataException("Unknown storage source in native preflight.");

        string root = CampaignPayloadLocation.ValidateRoot(Required(plan, "contentShareRoot"));
        string target = Required(plan, "packageStoragePath");
        if (!target.StartsWith(root + @"\", StringComparison.OrdinalIgnoreCase) ||
            Required(plan, "smbNtfsAclScan") != "PASS_ADVISORY_BROAD_WRITE_ACL_SCAN" ||
            Required(plan, "smbShareAclAccess") != "NOT_TESTED" ||
            Required(plan, "smbComputerAccountRead") != "NOT_TESTED" ||
            Required(plan, "smbStartupConnectivity") != "NOT_TESTED")
            throw new InvalidDataException("SMB preflight evidence is missing or inconsistent.");

        return "SMB SOURCE PREFLIGHT (READ ONLY)\r\n" +
            "Existing UNC root: " + root + "\r\n" +
            "New immutable package path: " + target + "\r\n" +
            "NTFS ancestry: PASS (ADVISORY broad-write Allow ACE scan only)\r\n" +
            "SMB share permissions: NOT TESTED\r\n" +
            "Actual computer-account READ access: NOT TESTED\r\n" +
            "Access during Windows startup: NOT TESTED\r\n" +
            "Before expanding rollout: use an approved Windows 10/11 LocalSystem canary, " +
            "verify machine-account SMB READ, reboot and inspect the GPO RSoP.\r\n" +
            "An administrator's successful SMB read is NOT proof of computer access.\r\n";
    }

    internal static string ConfirmationWarning(CampaignContentSource source, string? selectedRoot)
    {
        if (source != CampaignContentSource.SmbShare) return string.Empty;
        string root = CampaignPayloadLocation.ValidateRoot(selectedRoot);
        return "\r\nSMB UNC root: " + root + "\r\n" +
            "SECURITY LIMITATION: NTFS broad-write ACL inspection is advisory. " +
            "Share ACL, effective computer-account READ and startup SMB access " +
            "are NOT TESTED by Manager Preview.\r\n" +
            "Test the approved Windows 10/11 PC as LocalSystem before expanding.\r\n";
    }
}
