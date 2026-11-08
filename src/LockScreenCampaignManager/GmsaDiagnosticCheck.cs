using System.Net;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only, independently scheduled gMSA diagnostics. This class never
/// imports deployment scripts, updates the repair queue or invokes remote RPC.
/// The report is stored in the ACL-protected machine status folder.
/// </summary>
internal sealed class GmsaDiagnosticItem
{
    public string Name { get; set; } = "";
    public bool Passed { get; set; }
    public string Detail { get; set; } = "";
}

internal sealed class GmsaDiagnosticReport
{
    internal static string ReportPath =>
        Path.Combine(GmsaRepairConfig.StatusDirectory, "diagnostic.json");

    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset CompletedUtc { get; set; }
    // Bind the result to SID, domain, OU, group, SMB share and installed EXE.
    // A changed scope must never inherit an old successful diagnostic result.
    public string ScopeFingerprint { get; set; } = "";
    public List<GmsaDiagnosticItem> Checks { get; set; } = new();

    public bool Passed => Checks.Count > 0 && Checks.All(x => x.Passed);

    internal static GmsaDiagnosticReport? Load()
    {
        try
        {
            if (!File.Exists(ReportPath)) return null;
            var info = new FileInfo(ReportPath);
            if (info.Length is < 24 or > 16384 ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
                return null;
            var report = JsonSerializer.Deserialize<GmsaDiagnosticReport>(
                File.ReadAllText(ReportPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (report is null || report.StartedUtc.Year < 2025 ||
                report.CompletedUtc < report.StartedUtc ||
                report.CompletedUtc > DateTimeOffset.UtcNow.AddMinutes(5) ||
                report.ScopeFingerprint.Length != 64 ||
                !report.ScopeFingerprint.All(char.IsAsciiHexDigit) ||
                report.Checks is null ||
                report.Checks.Count is < 1 or > 12 ||
                report.Checks.Any(x => x is null || x.Name is null ||
                    x.Detail is null || x.Name.Length is < 1 or > 80 ||
                    x.Detail.Length > 200))
                return null;
            return report;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Read gMSA diagnostic report");
            return null;
        }
    }

    internal void Save()
    {
        if (!Directory.Exists(GmsaRepairConfig.StatusDirectory))
            throw new DirectoryNotFoundException(
                "The protected gMSA diagnostic status directory is missing.");
        string path = ReportPath;
        if (File.Exists(path) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Diagnostic status is a reparse point.");
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) {
                    WriteIndented = true
                }), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal string Format()
    {
        var result = new StringBuilder();
        result.AppendLine("gMSA read-only diagnostic — " +
            CompletedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        result.AppendLine(Passed ? "Result: PASS" : "Result: NEEDS ATTENTION");
        foreach (var item in Checks)
            result.AppendLine((item.Passed ? "[PASS] " : "[FAIL] ") +
                item.Name + ": " + item.Detail);
        result.AppendLine("No Remote RPC, client Repair or GPO changes were performed.");
        return result.ToString();
    }
}

internal static class GmsaDiagnosticCheck
{
    internal static string Fingerprint(GmsaRepairConfig config)
    {
        // Hash-only scope identifier; never write service-account or share
        // information to the diagnostic report. Stable across a reinstall
        // that leaves the protected scope unchanged.
        var values = new[] {
            config.GmsaSid, config.Domain, config.OuDn, config.GroupDn,
            config.FeedbackShare, config.ExecutablePath
        };
        var normalized = string.Join("\n", values.Select(x => x.Trim().ToUpperInvariant()));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    internal static bool BelongsTo(GmsaDiagnosticReport? report,
        GmsaRepairConfig config) =>
        report is not null && report.ScopeFingerprint.Length == 64 &&
        string.Equals(report.ScopeFingerprint, Fingerprint(config),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Testable exception-safe unit. Never records UNC paths, server addresses
    /// or account passwords in the diagnostic report.
    /// </summary>
    internal static GmsaDiagnosticItem Probe(string name, Action check,
        string success = "Available")
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
            throw new ArgumentException("Invalid diagnostic check name.");
        try
        {
            check();
            return new GmsaDiagnosticItem { Name = name, Passed = true,
                Detail = success };
        }
        catch (Exception ex)
        {
            return new GmsaDiagnosticItem { Name = name, Passed = false,
                Detail = UniversalDiagnosticsEmbedded.Clean(
                    ex.GetType().Name + ": " + ex.Message, 180) };
        }
    }

    private static void ConfirmReadableDirectory(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException("Directory unavailable.");
        // Exists() is not sufficient to prove that the gMSA can list the share.
        using var entries = Directory.EnumerateFileSystemEntries(path)
            .Take(1).GetEnumerator();
        _ = entries.MoveNext();
    }

    internal static GmsaDiagnosticReport Execute(GmsaRepairConfig config,
        DateTimeOffset startedUtc)
    {
        var report = new GmsaDiagnosticReport {
            StartedUtc = startedUtc, ScopeFingerprint = Fingerprint(config)
        };
        report.Checks.Add(Probe("gMSA process identity", () =>
        {
            string sid = WindowsIdentity.GetCurrent().User?.Value ?? "";
            if (!string.Equals(sid, config.GmsaSid, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Worker SID does not match configuration.");
        }, "Current process SID matches protected gMSA configuration"));

        report.Checks.Add(Probe("Protected executable and configuration", () =>
        {
            _ = GmsaRepairConfig.Load(requireWorkerIdentity: true);
        }, "Executable, configuration ACL and process identity verified"));

        report.Checks.Add(Probe("Protected repair queue", () =>
        {
            var items = GmsaRepairQueue.All(config);
            if (items.Count > 500)
                throw new InvalidDataException("Queue exceeds the safety limit.");
        }, "Queue is readable and internally valid"));

        report.Checks.Add(Probe("AD DNS", () =>
        {
            var addresses = Dns.GetHostAddresses(config.Domain);
            if (addresses.Length == 0)
                throw new InvalidOperationException("Domain DNS lookup returned no addresses.");
        }, "Domain name resolves via DNS"));

        report.Checks.Add(Probe("SYSVOL read access", () =>
        {
            ConfirmReadableDirectory(@"\\" + config.Domain + @"\SYSVOL");
        }, "SYSVOL can be enumerated with the gMSA token"));

        report.Checks.Add(Probe("Client Health SMB read access", () =>
        {
            string share = FeedbackPath.ValidateRoot(config.FeedbackShare);
            ConfirmReadableDirectory(share);
        }, "Configured feedback share can be enumerated"));

        report.CompletedUtc = DateTimeOffset.UtcNow;
        return report;
    }

    internal static int RunOnce()
    {
        // Exactly the same account, executable path, and ACL integrity checks
        // as the real unattended worker. No fallback to an admin session.
        try
        {
            var config = GmsaRepairConfig.Load(requireWorkerIdentity: true);
            var report = Execute(config, DateTimeOffset.UtcNow);
            report.Save();
            return report.Passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "gMSA read-only diagnostics startup");
            return 1;
        }
    }
}
