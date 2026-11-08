using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

internal static class BackgroundRepairIdentity
{
    internal static string CurrentSid() =>
        WindowsIdentity.GetCurrent().User?.Value ??
        throw new InvalidOperationException("Windows account SID could not be determined.");
}

/// <summary>
/// Explicit, per-user opt-in for a Windows Task Scheduler background check.
/// The task can run only while this same interactive Windows account is logged in.
/// No domain passwords, user logon tokens or machine-wide service credentials are stored.
/// </summary>
internal sealed class BackgroundRepairSettings
{
    internal static string? TestRootOverride;
    private static string DirectoryRoot => TestRootOverride ?? DeferredRepairQueue.DirectoryPath;
    private static string SettingsPath => Path.Combine(DirectoryRoot, "background-settings.json");
    public bool Enabled { get; set; }
    public string AccountSid { get; set; } = "";
    public string FeedbackShareRoot { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public DateTimeOffset EnabledUtc { get; set; }

    internal static BackgroundRepairSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            var info = new FileInfo(SettingsPath);
            if (info.Length is < 2 or > 8192) return new();
            var item = JsonSerializer.Deserialize<BackgroundRepairSettings>(File.ReadAllText(SettingsPath));
            if (item is null || item.AccountSid != BackgroundRepairIdentity.CurrentSid() ||
                item.Enabled && (item.EnabledUtc.Year < 2025 ||
                    string.IsNullOrWhiteSpace(item.ExecutablePath) ||
                    !Path.IsPathFullyQualified(item.ExecutablePath)))
                return new();
            if (item.Enabled)
                item.FeedbackShareRoot = FeedbackPath.ValidateRoot(item.FeedbackShareRoot);
            return item;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Background Repair settings");
            return new(); // Fail closed. A missing/corrupt preference is NEVER opt-in.
        }
    }

    internal void Save()
    {
        if (AccountSid != BackgroundRepairIdentity.CurrentSid())
            throw new InvalidOperationException("The background worker belongs to a different Windows account.");
        if (Enabled)
        {
            FeedbackShareRoot = FeedbackPath.ValidateRoot(FeedbackShareRoot);
            if (EnabledUtc.Year < 2025 || !Path.IsPathFullyQualified(ExecutablePath) ||
                !File.Exists(ExecutablePath))
                throw new InvalidOperationException("Invalid installed background worker executable path.");
        }
        Directory.CreateDirectory(DirectoryRoot);
        string temp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal bool Allows(DeferredRepairItem item) =>
        Enabled && AccountSid == BackgroundRepairIdentity.CurrentSid() &&
        item.BackgroundAllowed &&
        item.ApprovalSid == AccountSid &&
        item.ApprovedUtc >= EnabledUtc.AddMinutes(-5) &&
        item.ApprovedUtc <= DateTimeOffset.UtcNow.AddMinutes(5);
}
