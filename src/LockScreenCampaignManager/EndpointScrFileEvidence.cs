using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Optional, unsigned, LOCAL user-process read of one Screen Saver binary.
/// The probe never executes/loads the executable or contacts SMB/UNC paths.
/// A readable file does NOT verify screensaver functionality, code signature,
/// RSoP, effective privileges, or on-screen visual behavior.
/// </summary>
internal sealed record EndpointScrFileEvidence(
    string Source, string Status, string ValueSha256)
{
    private static readonly string[] States =
    [
        "Readable", "Missing", "Denied", "Linked", "NonLocal",
        "Unsupported", "Unresolved", "NotSelected", "Unavailable"
    ];

    private static EndpointUserSaverValue Selected(
        EndpointUserSaverValue policy, EndpointUserSaverValue desktop) =>
        policy.Status == "Unreadable" || desktop.Status == "Unreadable"
            ? new EndpointUserSaverValue("None", "SCRNSAVE.EXE", "Unreadable", "")
            : policy.Status == "Present" ? policy :
              desktop.Status == "Present" ? desktop :
                new EndpointUserSaverValue("None", "SCRNSAVE.EXE", "Missing", "");

    internal static EndpointScrFileEvidence Capture(
        EndpointUserSaverValue policy, EndpointUserSaverValue desktop)
    {
        var selected = Selected(policy, desktop);
        string status = selected.Status == "Unreadable" ? "Unavailable" :
            selected.Status != "Present" || string.IsNullOrWhiteSpace(selected.Value)
                ? "NotSelected" : ProbeLocalPath(selected.Value);
        return new EndpointScrFileEvidence(selected.Source, status,
            HashValue(selected.Value));
    }

    internal static EndpointScrFileEvidence Parse(
        JsonElement item, EndpointUserSaverValue policy,
        EndpointUserSaverValue desktop)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid local Screen Saver file observation.");
        string[] keys = item.EnumerateObject().Select(x => x.Name).ToArray();
        if (keys.Length != 3 || keys.Distinct(StringComparer.Ordinal).Count() != 3 ||
            !keys.Contains("source") || !keys.Contains("status") ||
            !keys.Contains("valueSha256"))
            throw new InvalidDataException("Unexpected SCR observation fields.");
        static string Field(JsonElement item, string key)
        {
            var value = item.GetProperty(key);
            if (value.ValueKind != JsonValueKind.String ||
                value.GetString() is not string text)
                throw new InvalidDataException("Invalid SCR observation field: " + key);
            return text;
        }
        string source = Field(item, "source"), status = Field(item, "status"),
            hash = Field(item, "valueSha256");
        var selected = Selected(policy, desktop);
        if (source != selected.Source || !States.Contains(status) ||
            hash.Length != 64 ||
            !hash.All(x => x is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            !hash.Equals(HashValue(selected.Value), StringComparison.Ordinal) ||
            (selected.Status != "Present" &&
                status is not ("NotSelected" or "Unavailable")) ||
            (selected.Status == "Present" &&
                string.IsNullOrWhiteSpace(selected.Value) && status != "NotSelected") ||
            (status == "Readable" &&
                (selected.Status != "Present" ||
                 string.IsNullOrWhiteSpace(selected.Value))))
            throw new InvalidDataException(
                "SCR observation differs from the captured HKCU registry value.");
        return new EndpointScrFileEvidence(source, status, hash);
    }

    private static string HashValue(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string ProbeLocalPath(string raw)
    {
        if (!OperatingSystem.IsWindows()) return "Unavailable";
        // Do not parse CLI arguments or execute SCR/EXE. Network shares,
        // device paths and mapped network drives cannot be read implicitly.
        try
        {
            string value = raw.Trim();
            if (value.Length > 512 || value.Any(char.IsControl)) return "Unsupported";
            if (value.StartsWith('"'))
            {
                if (value.Length < 3 || !value.EndsWith('"')) return "Unresolved";
                value = value[1..^1];
            }
            if (value.IndexOfAny(['"', '*', '?', '|', '<', '>']) >= 0)
                return "Unresolved";
            if (value.StartsWith(@"\\", StringComparison.Ordinal) ||
                value.StartsWith("//", StringComparison.Ordinal) ||
                value.StartsWith(@"\", StringComparison.Ordinal))
                return "NonLocal";
            value = Environment.ExpandEnvironmentVariables(value);
            if (value.Contains('%')) return "Unresolved";
            if (value.StartsWith(@"\\", StringComparison.Ordinal) ||
                value.StartsWith("//", StringComparison.Ordinal) ||
                value.StartsWith(@"\", StringComparison.Ordinal))
                return "NonLocal";
            string extension = Path.GetExtension(value);
            if (!extension.Equals(".scr", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                return "Unsupported";
            string full;
            if (Path.IsPathFullyQualified(value))
                full = Path.GetFullPath(value);
            else if (Path.GetFileName(value) == value && !value.Contains(':') &&
                     value is not ("." or ".."))
                full = Path.GetFullPath(Path.Combine(Environment.SystemDirectory, value));
            else return "Unresolved";
            string? root = Path.GetPathRoot(full);
            if (string.IsNullOrWhiteSpace(root) ||
                new DriveInfo(root).DriveType != DriveType.Fixed)
                return "NonLocal";
            var parent = new DirectoryInfo(Path.GetDirectoryName(full)!);
            for (DirectoryInfo? node = parent; node is not null; node = node.Parent)
                if ((node.Attributes & FileAttributes.ReparsePoint) != 0)
                    return "Linked";
            FileAttributes attrs = File.GetAttributes(full);
            if ((attrs & FileAttributes.ReparsePoint) != 0) return "Linked";
            if ((attrs & FileAttributes.Directory) != 0) return "Unsupported";
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            return stream.ReadByte() < 0 ? "Unavailable" : "Readable";
        }
        catch (UnauthorizedAccessException) { return "Denied"; }
        catch (SecurityException) { return "Denied"; }
        catch (FileNotFoundException) { return "Missing"; }
        catch (DirectoryNotFoundException) { return "Missing"; }
        catch (IOException) { return "Unavailable"; }
        catch (ArgumentException) { return "Unresolved"; }
        catch (NotSupportedException) { return "Unresolved"; }
    }
}
