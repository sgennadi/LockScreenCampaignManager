using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LockScreenCampaignManager;

/// <summary>
/// Explicitly imported, unsigned gpresult UserResults XML. The principal is
/// typed by the operator and MUST exactly match the reported UserResults.Name.
/// This never proves AD membership or association with any Computer RSoP.
/// </summary>
internal sealed class UserRsopEvidence
{
    private const string Namespace = "http://www.microsoft.com/GroupPolicy/Rsop";
    internal string Domain { get; private init; } = "";
    internal string UserPrincipal { get; private init; } = "";
    internal DateTimeOffset ReadUtc { get; private init; }
    internal DateTimeOffset ImportedUtc { get; private init; }
    internal string Sha256 { get; private init; } = "";
    internal IReadOnlyList<RsopGpoEvidence> Policies { get; private init; } = [];
    internal int UnidentifiedEntries { get; private init; }
    internal bool IsHistorical => DateTimeOffset.UtcNow - ReadUtc > TimeSpan.FromHours(24);
    internal bool IsSignedEndpointAttestation => false;
    internal bool ProvesActualSettingPrecedence => false;

    internal static UserRsopEvidence LoadLocalXml(string path,
        string expectedPrincipal, string expectedDomain)
    {
        var (document, sha256) = RsopEvidence.ReadLocalXmlDocument(path);
        return ParseDocument(document, expectedPrincipal, expectedDomain,
            DateTimeOffset.UtcNow, sha256);
    }

    internal static UserRsopEvidence ParseXml(string xml,
        string expectedPrincipal, string expectedDomain, DateTimeOffset now)
    {
        if (xml.Length is < 64 or > 12_000_000)
            throw new InvalidDataException("gpresult XML outside accepted size limits.");
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), RsopEvidence.SafeXml);
            return ParseDocument(XDocument.Load(reader, LoadOptions.None),
                expectedPrincipal, expectedDomain, now, "in-memory-fixture");
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("Unsafe or malformed gpresult XML.", ex);
        }
    }

    private static UserRsopEvidence ParseDocument(XDocument document,
        string expectedPrincipal, string expectedDomain,
        DateTimeOffset now, string sha)
    {
        if (string.IsNullOrWhiteSpace(expectedPrincipal) ||
            expectedPrincipal.Length > 256 ||
            expectedPrincipal.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(expectedDomain) ||
            expectedDomain.Length > 253 ||
            expectedDomain.Any(char.IsControl))
            throw new InvalidDataException("Expected User RSoP identity is missing/invalid.");

        string? netbios = null;
        int slash = expectedPrincipal.IndexOf('\\');
        int at = expectedPrincipal.IndexOf('@');
        if (slash > 0 && at < 0 && slash < expectedPrincipal.Length - 1 &&
            slash == expectedPrincipal.LastIndexOf('\\'))
            netbios = expectedPrincipal[..slash];
        else if (slash < 0 && at > 0 &&
                 at == expectedPrincipal.LastIndexOf('@') &&
                 expectedPrincipal[(at + 1)..].Equals(expectedDomain,
                     StringComparison.OrdinalIgnoreCase))
        { /* UPN is explicitly in the selected DNS domain. */ }
        else
            throw new InvalidDataException(
                "Enter the exact DOMAIN\\user or user@dns.domain from gpresult.");

        var root = document.Root;
        if (root is null || root.Name.LocalName != "Rsop" ||
            root.Name.NamespaceName != Namespace)
            throw new InvalidDataException("Not a supported gpresult Rsop XML document.");
        if (RsopEvidence.Text(root, "DataType") != "LoggedData")
            throw new InvalidDataException("Expected logged User RSoP, not planning data.");
        var users = root.Elements().Where(x => x.Name.LocalName == "UserResults").ToArray();
        if (users.Length != 1)
            throw new InvalidDataException("UserResults scope is missing or ambiguous.");
        string principal = RsopEvidence.Text(users[0], "Name") ?? "";
        if (!principal.Equals(expectedPrincipal, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "UserResults principal differs from the operator-selected user.");
        string reportedDomain = RsopEvidence.Text(users[0], "Domain") ?? "";
        if (reportedDomain.Length > 253 || reportedDomain.Any(char.IsControl) ||
            (reportedDomain.Length > 0 &&
             !reportedDomain.Equals(expectedDomain, StringComparison.OrdinalIgnoreCase) &&
             (netbios is null || !reportedDomain.Equals(netbios,
                 StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException(
                "UserResults domain disagrees with the explicitly selected user/domain.");

        string rawDate = RsopEvidence.Text(root, "ReadTime") ?? "";
        if (!DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset readAt) ||
            (rawDate.IndexOf('Z', StringComparison.OrdinalIgnoreCase) < 0 &&
             !Regex.IsMatch(rawDate, @"[+-]\d{2}:\d{2}\z")) ||
            readAt > now.AddMinutes(5) || readAt.Year < 2015)
            throw new InvalidDataException("Missing, ambiguous or future User RSoP time.");

        var entries = users[0].Elements().Where(x => x.Name.LocalName == "GPO").ToArray();
        if (entries.Length > 512)
            throw new InvalidDataException("Too many user GPO entries.");
        var policies = new List<RsopGpoEvidence>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int unidentified = 0;
        foreach (var item in entries)
        {
            string? id = RsopEvidence.GuidFromGpo(item);
            if (id is null) { unidentified++; continue; }
            if (!seen.Add(id))
                throw new InvalidDataException("Duplicate GPO GUID in UserResults.");
            string name = RsopEvidence.Text(item, "Name") ?? "";
            if (name.Length > 256 || name.Any(char.IsControl))
                throw new InvalidDataException("Invalid user GPO display name.");

            bool? enabled = RsopEvidence.Bool(item, "Enabled");
            bool? valid = RsopEvidence.Bool(item, "IsValid");
            bool? allowed = RsopEvidence.Bool(item, "FilterAllowed");
            bool? denied = RsopEvidence.Bool(item, "AccessDenied");
            string observation, reason;
            if (enabled == false || valid == false || allowed == false || denied == true)
            {
                observation = "Reported filtered";
                reason = enabled == false ? "User GPO disabled" :
                    valid == false ? "Invalid User GPO" :
                    denied == true ? "Access denied" : "Filtered by client";
            }
            else if (enabled == true && valid == true &&
                     allowed == true && denied == false)
            {
                observation = "Reported applied";
                reason = "UserResults GPO flags report application; not the winning setting.";
            }
            else
            {
                observation = "Insufficient flags";
                reason = "Missing UserResults flags; not proof of application.";
            }
            policies.Add(new RsopGpoEvidence(id, name, observation, reason));
        }
        return new UserRsopEvidence
        {
            Domain = expectedDomain, UserPrincipal = expectedPrincipal,
            ReadUtc = readAt.ToUniversalTime(), ImportedUtc = now,
            Sha256 = sha,
            Policies = policies.OrderBy(x => x.Name,
                StringComparer.OrdinalIgnoreCase).ToArray(),
            UnidentifiedEntries = unidentified
        };
    }

    internal string ObservationFor(string id)
    {
        if (!Guid.TryParse(id, out Guid parsed) || parsed == Guid.Empty)
            return "Invalid GUID";
        return Policies.SingleOrDefault(x => Guid.TryParse(x.Id, out Guid candidate) &&
            candidate == parsed)?.Observation ?? "Not listed (unknown)";
    }
    internal string DetailFor(string id)
    {
        if (!Guid.TryParse(id, out Guid parsed) || parsed == Guid.Empty)
            return "Invalid GUID";
        return Policies.SingleOrDefault(x => Guid.TryParse(x.Id, out Guid candidate) &&
            candidate == parsed)?.Reason ??
            "GUID absent in imported UserResults; not evidence of non-application.";
    }
}
