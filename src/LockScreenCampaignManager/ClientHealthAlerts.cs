using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Notifications are strictly local to an open manager. The SMB files are untrusted
/// status hints, so alerts NEVER trigger remote writes or remediation on their own.
/// </summary>
internal sealed class ClientAlertPreferences
{
    public bool Enabled { get; set; } = false;
    public int NewErrorsThreshold { get; set; } = 2;
    public int CooldownMinutes { get; set; } = 30;
    public bool IncludeStale { get; set; } = false;
    private static readonly string SettingsPath = Path.Combine(AppLog.DirectoryPath, "client-alerts.json");

    public ClientAlertPreferences Validate()
    {
        NewErrorsThreshold = Math.Clamp(NewErrorsThreshold, 1, 50);
        CooldownMinutes = Math.Clamp(CooldownMinutes, 5, 240);
        return this;
    }

    public static ClientAlertPreferences Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new ClientAlertPreferences();
            var file = new FileInfo(SettingsPath);
            if (file.Length > 8192) throw new InvalidDataException("Unexpected oversized alert settings file.");
            return (JsonSerializer.Deserialize<ClientAlertPreferences>(File.ReadAllText(SettingsPath)) ?? new()).Validate();
        }
        catch (Exception e) { AppLog.Write(e, "Read alert settings"); return new ClientAlertPreferences(); }
    }

    public void Save()
    {
        Validate();
        Directory.CreateDirectory(AppLog.DirectoryPath);
        string temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal sealed class ClientAlertEngine
{
    private string? _campaignId;
    private int _baseline;
    private DateTimeOffset _lastShownUtc = DateTimeOffset.MinValue;

    internal void Reset() => _campaignId = null;

    // First trusted snapshot establishes the baseline. No noise on open or when
    // switching campaigns. Cooldown prevents flooding the admin with balloons.
    internal string? Observe(ClientHealthSnapshot snap, ClientAlertPreferences prefs,
        DateTimeOffset nowUtc)
    {
        if (!prefs.Enabled || snap.Expected == 0) { Reset(); return null; }
        int current = snap.Errors + (prefs.IncludeStale ? snap.Stale : 0);
        if (!string.Equals(_campaignId, snap.CampaignId, StringComparison.Ordinal))
        {
            _campaignId = snap.CampaignId;
            _baseline = current;
            _lastShownUtc = DateTimeOffset.MinValue;
            return null;
        }
        if (current < _baseline) { _baseline = current; return null; }
        if (current - _baseline < prefs.NewErrorsThreshold ||
            nowUtc - _lastShownUtc < TimeSpan.FromMinutes(prefs.CooldownMinutes))
            return null;
        int increase = current - _baseline;
        _baseline = current;
        _lastShownUtc = nowUtc;
        return $"Campaign {snap.CampaignId}: +{increase} problems. " +
               $"Errors: {snap.Errors}, stale: {snap.Stale}, no report: {snap.NoReport}.";
    }
}
