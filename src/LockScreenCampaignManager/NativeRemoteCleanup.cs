using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only post-cleanup evidence verification on selected, AD-scoped hosts.
/// SMB, Schedule.Service RPC and explicit Registry64 reads only; never executes
/// remote tasks or changes registry, files, GPO, AD or client service state.
/// </summary>
internal static class NativeRemoteCleanup
{
    internal sealed record Result(
        string computer, string campaignId, string observedUtc, string status,
        string reason, bool receiptValid, bool tasksGone, bool localFolderGone,
        bool ownerReleased, bool registryRestored);

    internal static Result[] CheckMany(IReadOnlyList<string> computers, string campaignId)
    {
        if (!NativeSysvolStorage.ValidCampaignId(campaignId))
            throw new InvalidDataException("Invalid verified campaign identifier.");
        if (computers.Count is < 1 or > 25 ||
            computers.Any(x => string.IsNullOrWhiteSpace(x)))
            throw new ArgumentException("Select 1-25 verified AD computers.");
        return computers.Select(host => CheckOne(host, campaignId)).ToArray();
    }

    private static Result CheckOne(string host, string campaignId)
    {
        bool receipt = false, tasks = false, folderGone = false;
        bool ownerReleased = false, registryRestored = false;
        var observed = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        Result Finish(string status, string reason) =>
            new(host, campaignId, observed, status, reason,
                receipt, tasks, folderGone, ownerReleased, registryRestored);
        try
        {
            // Host must be selected from AD eligible list upstream. Re-apply
            // syntactic guard here to prevent SMB UNC path injections.
            if (host.Length is < 3 or > 253 ||
                !NativeDcConsistency.ValidController(host,
                    host[(host.IndexOf('.') + 1)..]) ||
                !NativeSysvolStorage.ValidCampaignId(campaignId))
                return Finish("Unknown", "ReadDeniedOrInvalidEvidence");

            string root = @"\\" + host + @"\C$\ProgramData\LockScreenCampaigns";
            var attrs = File.GetAttributes(root);
            if ((attrs & FileAttributes.Directory) == 0 ||
                (attrs & FileAttributes.ReparsePoint) != 0)
                return Finish("Unknown", "ReadDeniedOrInvalidEvidence");

            // Directory.EnumerateFileSystemEntries must finish successfully:
            // File.Exists alone is not evidence of remote folder absence.
            string[] entries = Directory.GetFileSystemEntries(root);
            if (entries.Any(x => string.Equals(Path.GetFileName(x),
                    campaignId, StringComparison.Ordinal)))
                return Finish("Pending", "LocalCampaignFolderStillPresent");
            folderGone = true;

            string? ownerText = ReadOptionalFile(Path.Combine(root, "active-owner.json"), 8192);
            if (ownerText is not null)
            {
                using var owner = JsonDocument.Parse(ownerText, new JsonDocumentOptions { MaxDepth = 12 });
                string activeId = GetString(owner.RootElement, "CampaignId");
                return string.Equals(activeId, campaignId, StringComparison.Ordinal)
                    ? Finish("Pending", "CampaignOwnerStillPresent")
                    : Finish("NeedsReview", "AnotherCampaignOwnsRegistry");
            }
            ownerReleased = true;

            string proofPath = Path.Combine(root, "Completed", campaignId + ".postcleanup.json");
            string? proof = ReadOptionalFile(proofPath, 16384);
            if (proof is null) return Finish("Pending", "NoPostCleanupReceipt");
            using var doc = JsonDocument.Parse(proof, new JsonDocumentOptions { MaxDepth = 12 });
            string? problem = ValidateReceipt(doc.RootElement, campaignId, DateTimeOffset.UtcNow);
            if (problem is not null) return Finish("NeedsReview", problem);
            receipt = true;

            tasks = TasksGone(host, campaignId);
            if (!tasks) return Finish("Pending", "CampaignTaskStillPresent");

            using var registry = RegistryKey.OpenRemoteBaseKey(
                RegistryHive.LocalMachine, host, RegistryView.Registry64);
            using var policy = registry.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows\Personalization", false);
            registryRestored = CompareRegistryValue(policy, "LockScreenImage",
                                   Get(doc.RootElement, "expectedLockScreenImage")) &&
                               CompareRegistryValue(policy, "NoChangingLockScreen",
                                   Get(doc.RootElement, "expectedNoChangingLockScreen"));
            return registryRestored
                ? Finish("Verified", "RemoteFootprintMatchesPostCleanupReceipt")
                : Finish("NeedsReview", "RegistryDiffersFromReceipt");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or JsonException or ArgumentException
            or System.ComponentModel.Win32Exception or COMException
            or TargetInvocationException or InvalidOperationException
            or System.Security.SecurityException or PlatformNotSupportedException)
        {
            int hr = ex is TargetInvocationException tie && tie.InnerException is not null
                ? tie.InnerException.HResult : ex.HResult;
            return Finish("Unknown",
                IsOfflineHresult(hr) ? "ClientOfflineOrUnreachable" :
                "ReadDeniedOrInvalidEvidence");
        }
    }

    private static bool IsOfflineHresult(int hr)
    {
        uint value = unchecked((uint)hr);
        return value is 0x800706BA or 0x800706BE or 0x800704CF or
            0x80070035 or 0x800705B4 or 0x80070040 or 0x8007274C;
    }

    internal static string? ValidateReceipt(
        JsonElement receipt, string campaignId, DateTimeOffset now)
    {
        if (receipt.ValueKind != JsonValueKind.Object ||
            GetInt(receipt, "schemaVersion") != 1 ||
            !string.Equals(GetString(receipt, "campaignId"), campaignId,
                StringComparison.Ordinal) ||
            GetString(receipt, "cleanupStage") != "AfterFileAndTaskRemoval" ||
            !GetBool(receipt, "localRootRemoved") ||
            !GetBool(receipt, "scheduledTaskRemoved") ||
            !GetBool(receipt, "feedbackTaskRemoved") ||
            !GetBool(receipt, "ownerReleased") ||
            !GetBool(receipt, "restoredOwnRegistry") ||
            Get(receipt, "expectedLockScreenImage").ValueKind != JsonValueKind.Object ||
            Get(receipt, "expectedNoChangingLockScreen").ValueKind != JsonValueKind.Object)
            return "IncompleteOrUnrestoredReceipt";

        string stamp = GetString(receipt, "observedUtc");
        if (!DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var at) ||
            at > now.AddMinutes(5))
            return "InvalidReceiptTime";
        return null;
    }

    internal static bool MatchesExpected(bool exists, RegistryValueKind kind,
        object? actual, JsonElement expected)
    {
        if (expected.ValueKind != JsonValueKind.Object ||
            Get(expected, "Exists").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            Get(expected, "Kind").ValueKind != JsonValueKind.String)
            return false;

        bool expectedExists = GetBool(expected, "Exists");
        if (exists != expectedExists) return false;
        if (!expectedExists) return true;
        string expectedKind = GetString(expected, "Kind");
        if (!string.Equals(kind.ToString(), expectedKind, StringComparison.Ordinal) ||
            kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString
                or RegistryValueKind.DWord or RegistryValueKind.QWord
                or RegistryValueKind.Binary or RegistryValueKind.MultiString))
            return false;

        var value = Get(expected, "Value");
        return kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString =>
                value.ValueKind == JsonValueKind.String && actual is string str &&
                string.Equals(str, value.GetString(), StringComparison.Ordinal),
            RegistryValueKind.DWord =>
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out int i) && actual is int v && i == v,
            RegistryValueKind.QWord =>
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt64(out long i) && actual is long v && i == v,
            RegistryValueKind.Binary => actual is byte[] bytes &&
                value.ValueKind == JsonValueKind.Array &&
                value.GetArrayLength() == bytes.Length &&
                value.EnumerateArray().Select(x =>
                    x.ValueKind == JsonValueKind.Number &&
                    x.TryGetByte(out byte b) ? (int)b : -1)
                    .SequenceEqual(bytes.Select(x => (int)x)),
            RegistryValueKind.MultiString => actual is string[] strings &&
                value.ValueKind == JsonValueKind.Array &&
                value.GetArrayLength() == strings.Length &&
                value.EnumerateArray().Select(x =>
                    x.ValueKind == JsonValueKind.String ? x.GetString() : null)
                    .SequenceEqual(strings),
            _ => false
        };
    }

    private static bool CompareRegistryValue(
        RegistryKey? key, string name, JsonElement expected)
    {
        if (key is null || !key.GetValueNames().Contains(name, StringComparer.Ordinal))
            return MatchesExpected(false, RegistryValueKind.String, null, expected);
        return MatchesExpected(true, key.GetValueKind(name),
            key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames),
            expected);
    }

    private static string? ReadOptionalFile(string path, int maximum)
    {
        FileAttributes attr;
        try { attr = File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        if ((attr & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Unexpected remote proof file type.");
        var info = new FileInfo(path);
        if (info.Length is < 8 || info.Length > maximum)
            throw new InvalidDataException("Remote proof file has unexpected size.");
        using var stream = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.Read);
        if (stream.Length != info.Length)
            throw new InvalidDataException("Remote proof file changed while reading.");
        byte[] bytes = new byte[checked((int)info.Length)];
        stream.ReadExactly(bytes);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static bool TasksGone(string host, string campaignId)
    {
        object? service = null;
        object? root = null;
        try
        {
            Type server = Type.GetTypeFromProgID("Schedule.Service", true) ??
                throw new InvalidOperationException("Windows Task Scheduler COM is missing.");
            service = Activator.CreateInstance(server) ??
                throw new InvalidOperationException("Cannot activate Task Scheduler COM.");
            Call(service, "Connect", host);
            root = Call(service, "GetFolder", @"\");
            if (root is null) throw new InvalidDataException("Task Scheduler root unavailable.");
            foreach (string task in new[]
            {
                "LockScreen-LS-" + campaignId,
                "LockScreen-LS-Feedback-" + campaignId,
                "LSCM-Now-" + campaignId
            })
            {
                object? found = null;
                try
                {
                    found = Call(root, "GetTask", task);
                    if (found is not null) return false;
                }
                catch (TargetInvocationException ex) when
                    (ex.InnerException is COMException com &&
                     unchecked((uint)com.HResult) == 0x80070002)
                {
                    // Genuine not found from Task Scheduler, not an RPC error.
                }
                catch (COMException ex) when
                    (unchecked((uint)ex.HResult) == 0x80070002)
                {
                    // Some COM dispatchers propagate the HRESULT directly.
                }
                finally { Release(found); }
            }
            return true;
        }
        finally
        {
            Release(root);
            Release(service);
        }
    }

    private static object? Call(object obj, string name, params object[] args) =>
        obj.GetType().InvokeMember(name, BindingFlags.InvokeMethod,
            null, obj, args, CultureInfo.InvariantCulture);

    private static void Release(object? obj)
    {
        if (obj is not null && Marshal.IsComObject(obj))
            Marshal.FinalReleaseComObject(obj);
    }

    private static JsonElement Get(JsonElement root, string key)
    {
        if (root.ValueKind != JsonValueKind.Object) return default;
        if (root.TryGetProperty(key, out var value)) return value;
        foreach (var prop in root.EnumerateObject())
            if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                return prop.Value;
        return default;
    }
    private static string GetString(JsonElement root, string key) =>
        Get(root, key) is { ValueKind: JsonValueKind.String } prop ?
            prop.GetString() ?? "" : "";
    private static bool GetBool(JsonElement root, string key) =>
        Get(root, key).ValueKind == JsonValueKind.True;
    private static int GetInt(JsonElement root, string key) =>
        Get(root, key) is { ValueKind: JsonValueKind.Number } prop &&
        prop.TryGetInt32(out int value) ? value : -1;
}
