using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Campaign *payload* location, never the GPO's own GPT/SYSVOL metadata.
/// SMB roots are administrator-provisioned; computers must have READ access.
/// Reject unsafe UNC roots and broadly writable script directories.
/// </summary>
internal static class CampaignPayloadLocation
{
    private static readonly Regex ServerName = new(
        @"\A[A-Za-z0-9](?:[A-Za-z0-9.-]{0,251}[A-Za-z0-9])?\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Component = new(
        @"\A[A-Za-z0-9_][A-Za-z0-9_. -]{0,79}\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex DomainWideSid = new(
        @"\AS-1-5-21-(?:[0-9]+-){3}(?:513|515)\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static CampaignContentSource Parse(string? value) => value switch
    {
        null or "" or "Sysvol" => CampaignContentSource.Sysvol,
        "SmbShare" => CampaignContentSource.SmbShare,
        _ => throw new InvalidDataException("Unrecognized campaign content source.")
    };

    internal static string ValidateRoot(string? value)
    {
        string root = (value ?? "").Trim().TrimEnd('\\');
        if (!root.StartsWith(@"\\", StringComparison.Ordinal) ||
            root.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            root.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            root.Length is < 8 or > 200 ||
            root.Any(char.IsControl) || root.Contains('/') || root.Contains(':'))
            throw new CampaignValidationException(
                @"Select a regular UNC folder such as \\files.example.com\Campaigns.");
        string[] parts = root[2..].Split('\\');
        if (parts.Length is < 2 or > 8 ||
            !ServerName.IsMatch(parts[0]) || parts[0].Contains("..", StringComparison.Ordinal))
            throw new CampaignValidationException("SMB server name is not valid.");
        for (int i = 1; i < parts.Length; i++)
        {
            if (!Component.IsMatch(parts[i]) || parts[i] is "." or ".." ||
                parts[i].EndsWith(' ') || parts[i].EndsWith('.') ||
                parts[i].Contains("..", StringComparison.Ordinal) ||
                parts[i].Contains('$') || parts[i].Contains('[') ||
                parts[i].Contains(']') || parts[i].Contains(';'))
                throw new CampaignValidationException("SMB share or folder name is unsafe.");
        }
        if (parts.Skip(1).Any(s => s.Equals("SYSVOL", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("NETLOGON", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("IPC", StringComparison.OrdinalIgnoreCase)))
            throw new CampaignValidationException(
                "Use a dedicated SMB content share, not SYSVOL/NETLOGON.");
        return root;
    }

    internal static string TargetPath(string root, string id)
    {
        if (!NativeSysvolStorage.ValidCampaignId(id))
            throw new InvalidDataException("Unsafe campaign identifier.");
        root = ValidateRoot(root);
        string path = root + "\\" + id;
        if (path.Length + "\\LockScreen-Agent.ps1".Length >= 260 ||
            path.Length + "\\LockScreen-Agent.exe".Length >= 260)
            throw new CampaignValidationException("SMB startup path is too long.");
        return path;
    }

    internal static void CheckShareReadOnly(string root)
    {
        root = ValidateRoot(root);
        // Read-only preflight: do not test write access by creating anything.
        // Check all visible ancestors from the share root to the selected folder.
        string[] parts = root[2..].Split('\\');
        string ancestor = @"\\" + parts[0] + "\\" + parts[1];
        for (int i = 2; ; i++)
        {
            var info = new DirectoryInfo(ancestor);
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("SMB campaign directory is missing or linked.");
            var acl = info.GetAccessControl(AccessControlSections.Access);
            if (acl is null)
                throw new InvalidDataException("Cannot verify SMB NTFS permissions.");
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(
                includeExplicit: true, includeInherited: true,
                targetType: typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow)
                    continue;
                string sid = rule.IdentityReference.Value;
                bool broad = sid is "S-1-1-0" or "S-1-5-11" or
                    "S-1-5-32-545" or "S-1-5-32-546" ||
                    DomainWideSid.IsMatch(sid);
                const FileSystemRights unsafeRights =
                    FileSystemRights.WriteData | FileSystemRights.AppendData |
                    FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes |
                    FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
                    FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
                if (broad && (rule.FileSystemRights & unsafeRights) != 0)
                    throw new InvalidDataException(
                        "SMB payload has broad write permissions. Grant Domain Computers READ only.");
            }
            if (i >= parts.Length) break;
            ancestor += "\\" + parts[i];
        }
        // Even a correctly secured NTFS folder does not prove share-level
        // permissions or endpoint access as the COMPUTER account.
    }
}
