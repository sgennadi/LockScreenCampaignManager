namespace LockScreenCampaignManager;

/// <summary>
/// Immutable, in-memory comparison between a selected-DC SYSVOL source
/// inventory and voluntarily imported, unsigned Computer/User gpresult XML.
/// GUID+scope identity only. Never computes winning per-setting policy.
/// </summary>
internal sealed record DomainGpoRsopMatch(
    string Scope, string Gpo, string GpoId, string RegistryKey,
    string Setting, string Value, string SourceRisk, string LinkStatus,
    string ReportedRsop, string Interpretation, string Source);

internal static class DomainGpoRsopCorrelation
{
    internal static IReadOnlyList<DomainGpoRsopMatch> Compare(
        DomainWideGpoReport report, RsopEvidence? computer, UserRsopEvidence? user)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (computer is not null &&
            !computer.Domain.Equals(report.Domain, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Imported computer domain is not the scanned GPO domain.");
        if (user is not null &&
            !user.Domain.Equals(report.Domain, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Imported user domain is not the scanned GPO domain.");
        return report.Matches.Select(x =>
        {
            string observed = x.Scope == "Computer"
                ? computer?.ObservationFor(x.GpoId) ?? "Not imported"
                : user?.ObservationFor(x.GpoId) ?? "Not imported";
            string note = observed switch
            {
                "Reported applied" =>
                    "Reported applied in unsigned, point-in-time gpresult XML. " +
                    "Per-setting precedence, source version and live behavior NOT verified.",
                "Reported filtered" =>
                    "Imported client flags report GPO filtered. This does not prove " +
                    "the source cannot apply at another time or to another device/user.",
                "Insufficient flags" =>
                    "Client gpresult flags are insufficient to conclude application.",
                "Not listed (unknown)" =>
                    "GPO GUID is absent from this scope's imported XML; never a proven exclusion.",
                _ => "No imported XML for this scope."
            };
            return new DomainGpoRsopMatch(
                x.Scope, x.Gpo, x.GpoId, x.Key, x.Setting, x.Value,
                x.Risk, x.LinkStatus, observed, note, x.Source);
        }).ToArray();
    }

    internal static string EvidenceWarning(RsopEvidence? computer, UserRsopEvidence? user)
    {
        var messages = new List<string>();
        if (computer is null) messages.Add("Computer RSoP not imported.");
        else messages.Add("Computer: " + computer.Computer + ", " +
            computer.ReadUtc.ToString("u") +
            (computer.IsHistorical ? " [HISTORICAL]" : "") +
            "; SHA-256 " + computer.Sha256);
        if (user is null) messages.Add("User RSoP not imported.");
        else messages.Add("User: " + user.UserPrincipal + ", " +
            user.ReadUtc.ToString("u") +
            (user.IsHistorical ? " [HISTORICAL]" : "") +
            "; SHA-256 " + user.Sha256);
        messages.Add("Neither imported file is signed or remotely collected. " +
            "User/Computer evidence may describe different machines or sessions. " +
            "GPO source settings are NOT necessarily winning RSoP settings.");
        return string.Join(Environment.NewLine, messages);
    }
}
