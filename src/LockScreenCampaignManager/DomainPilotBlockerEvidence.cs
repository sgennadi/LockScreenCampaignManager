using System.Globalization;
using System.Text;

namespace LockScreenCampaignManager;

/// <summary>
/// Named read-only GPO blocker candidates for a selected Computer/User pilot.
/// GPO GUID + scope matches unsigned, manually imported gpresult XML. This
/// cannot resolve per-setting precedence, targeting, GPP item-level scope,
/// real HKCU, MDM, or current physical lock/saver display state.
/// </summary>
internal sealed record DomainPilotBlocker(
    string Scope, string Gpo, string GpoId,
    string RegistryKey, string Setting, string Value,
    string Risk, string ReportedRsop, string ReviewState,
    string Source, string LinkStatus, string Interpretation);

internal static class DomainPilotBlockerEvidence
{
    internal static IReadOnlyList<DomainPilotBlocker> Analyze(
        IReadOnlyList<DomainGpoRsopMatch> sourceRows,
        DateTimeOffset scanUtc, DateTimeOffset now,
        RsopEvidence? computer, UserRsopEvidence? user)
    {
        ArgumentNullException.ThrowIfNull(sourceRows);
        if (sourceRows.Count > 6000)
            throw new InvalidDataException("Unbounded domain GPO blocker evidence.");
        bool currentScan = scanUtc <= now.AddMinutes(2) &&
            now - scanUtc <= TimeSpan.FromMinutes(15);
        return sourceRows.Where(row =>
                row.LinkStatus == "Linked candidate" &&
                row.SourceRisk is ("High" or "Warning"))
            .Select(row =>
            {
                bool hasXml = row.Scope == "Computer" ? computer is not null : user is not null;
                DateTimeOffset? readUtc = row.Scope == "Computer"
                    ? computer?.ReadUtc : user?.ReadUtc;
                bool currentXml = hasXml && readUtc.HasValue &&
                    readUtc.Value <= now.AddMinutes(5) &&
                    now - readUtc.Value <= TimeSpan.FromHours(24);
                string review = !currentScan ? "Historical GPO source" :
                    row.ReportedRsop == "Reported filtered" ? "Reported filtered" :
                    row.ReportedRsop == "Reported applied" && currentXml
                        ? "Review first (GPO reported applied)" :
                    row.ReportedRsop == "Reported applied"
                        ? "Historical gpresult" : "Unverified applicability";
                // Even "Review first" must NOT be described as proven blocker.
                return new DomainPilotBlocker(
                    row.Scope, row.Gpo, row.GpoId, row.RegistryKey,
                    row.Setting, row.Value, row.SourceRisk,
                    row.ReportedRsop, review, row.Source, row.LinkStatus,
                    row.Interpretation + " No confirmed blocking GPO or winning setting.");
            })
            .OrderBy(x => x.ReviewState switch {
                "Review first (GPO reported applied)" => 0,
                "Unverified applicability" => 1,
                "Historical gpresult" => 2,
                "Historical GPO source" => 3,
                _ => 4
            })
            .ThenBy(x => x.Risk == "High" ? 0 : 1)
            .ThenBy(x => x.Scope, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Gpo, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Setting, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string BuildCsv(IEnumerable<DomainPilotBlocker> blockers)
    {
        ArgumentNullException.ThrowIfNull(blockers);
        static string Cell(string value)
        {
            string normalized = value.Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Replace("\t", " ", StringComparison.Ordinal);
            // Untrusted domain GPO names/values can become spreadsheet formulas.
            if (normalized.Length != 0 && "=+-@".Contains(normalized[0]))
                normalized = "'" + normalized;
            return "\"" + normalized.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }
        var csv = new StringBuilder(
            "Scope,GPO,GPO GUID,Setting,Configured value,Registry key,Source risk," +
            "Imported gpresult state,Review priority,Policy source,Link status,Limitations\r\n");
        int count = 0;
        foreach (var entry in blockers)
        {
            if (++count > 6000)
                throw new InvalidDataException("Too many GPO records for CSV.");
            string[] fields = [entry.Scope,entry.Gpo,entry.GpoId,entry.Setting,
                entry.Value,entry.RegistryKey,entry.Risk,entry.ReportedRsop,
                entry.ReviewState,entry.Source,entry.LinkStatus,
                entry.Interpretation];
            csv.Append(string.Join(",",fields.Select(Cell))).Append("\r\n");
        }
        return csv.ToString();
    }

    internal static void ExportLocalCsv(
        string path, IReadOnlyCollection<DomainPilotBlocker> blockers)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a local CSV filename.");
        string full = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(full) ||
            full.StartsWith(@"\\", StringComparison.Ordinal) ||
            full.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            full.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new InvalidDataException("Only a normal LOCAL folder is allowed.");
        if (blockers.Count > 6000)
            throw new InvalidDataException("Unbounded CSV export.");
        for (DirectoryInfo? directory = new(Path.GetDirectoryName(full)!);
             directory is not null && directory.Parent is not null;
             directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "Do not save private domain GPO evidence through linked folders.");
        }
        if (File.Exists(full) &&
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Cannot overwrite a linked CSV file.");
        File.WriteAllText(full, BuildCsv(blockers),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
