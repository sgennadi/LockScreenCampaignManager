using System.Globalization;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>Display-only fleet strings. No localized value is used for
/// authorization, filtering, report serialization or task status decisions.</summary>
internal static partial class FleetStatusPresentation
{
    private static readonly Regex FileCounts = new(
        @"\AMissing ([0-9]+), Corrupt ([0-9]+)\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static string T(string key, string language)
    {
        if (!Strings.TryGetValue(key, out var translated)) return key;
        return language == "Russian" ? translated.Russian :
            language == "Hebrew" ? translated.Hebrew : key;
    }

    internal static string Format(string key, string language, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, T(key, language), args);

    internal static string ClientType(string value, string language) =>
        value == "Unknown" ? T(value, language) : value;

    internal static string SubmissionKey(string status) =>
        status.ToLowerInvariant() switch {
            "success" => "Submission accepted",
            "pending" => "Submission pending",
            "offline" => "Submission unavailable",
            "failed" => "Submission failed",
            _ => status
        };

    internal static string HealthCell(int column, string raw, string language)
    {
        // Never translate computer names, timestamps, JPG filenames or versions,
        // even if the literal happens to match a translatable word such as Active.
        if (column == 9) return ClientType(raw, language);
        if (column is 1 or 2 or 6 or 7) return T(raw, language);
        if (column != 5) return raw;
        var match = FileCounts.Match(raw);
        return match.Success
            ? Format("Missing {0}, Corrupt {1}", language,
                match.Groups[1].Value, match.Groups[2].Value)
            : T(raw, language);
    }

    internal static string ReportDetails(AgentReportEvidence evidence, string language)
    {
        // Preserve the existing canonical raw English description.
        if (language == "English") return evidence.Details;
        if (evidence.Facts is not { } facts) return T(evidence.Details, language);
        return Format(
            "Client-reported: {0} / {1} | client method {2} | image {3} | registry {4} | task {5} | missing files {6} | corrupt files {7} | timestamp supplied by endpoint; not authenticated proof.",
            language, T(evidence.Stage, language), T(evidence.Health, language),
            ClientType(evidence.ClientType, language),
            T(facts.ImageMatches ? "match" : "not verified", language),
            T(facts.RegistryMatches ? "match" : "not verified", language),
            T(facts.TaskPresent ? "present" : "missing", language),
            facts.MissingFiles, facts.CorruptFiles);
    }
}
