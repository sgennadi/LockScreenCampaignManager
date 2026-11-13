using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LockScreenCampaignManager;

/// <summary>
/// Manually supplied local GPRESULT /X ComputerResults snapshot. Imported
/// files are untrusted and never attest to the current endpoint state.
/// No remote operations, and no fallback from GPO name to GUID matching.
/// </summary>
internal sealed record RsopGpoEvidence(string Id, string Name, string Observation, string Reason);

internal sealed class RsopEvidence
{
    private const long MaxBytes = 12L * 1024 * 1024;
    private const string RsopNamespace = "http://www.microsoft.com/GroupPolicy/Rsop";
    private static readonly Regex GuidPattern = new(
        @"\{(?<id>[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12})\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly XmlReaderSettings SafeXml = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        MaxCharactersInDocument = 20_000_000,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true
    };
    internal string Computer { get; private init; } = "";
    internal string Domain { get; private init; } = "";
    internal DateTimeOffset ReadUtc { get; private init; }
    internal DateTimeOffset ImportedUtc { get; private init; }
    internal string Sha256 { get; private init; } = "";
    internal IReadOnlyList<RsopGpoEvidence> Policies { get; private init; } = [];
    internal int UnidentifiedEntries { get; private init; }
    internal bool IsHistorical => DateTimeOffset.UtcNow - ReadUtc > TimeSpan.FromHours(24);
    internal bool IsSignedEndpointAttestation => false;
    internal bool HasCompleteSettingPrecedenceEvidence => false;

    internal static RsopEvidence LoadLocalXml(string path, string expectedComputer, string expectedDomain)
    {
        var (document, hash) = ReadLocalXmlDocument(path);
        return ParseDocument(document, expectedComputer, expectedDomain,
            DateTimeOffset.UtcNow, hash);
    }

    /// <summary>
    /// Shared bounded XML read for explicit Computer and User gpresult import.
    /// Only returns a parsed document and checksum; no file is stored or
    /// executed. The source file itself is NOT authenticated by SHA-256.
    /// </summary>
    internal static (XDocument Document, string Sha256) ReadLocalXmlDocument(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !string.Equals(Path.GetExtension(path), ".xml",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a local gpresult /x XML report.");
        string full = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(full) ||
            full.StartsWith(@"\\", StringComparison.Ordinal) ||
            full.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            full.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Select a regular local XML file, not a network or device path.");
        for (var folder = new DirectoryInfo(Path.GetDirectoryName(full)!);
             folder is not null && folder.Parent is not null; folder = folder.Parent)
            if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked folder is not accepted for RSoP import.");
        var file = new FileInfo(full);
        if (!file.Exists || file.Length is < 64 or > MaxBytes ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
            (file.Directory is { } dir &&
                (dir.Attributes & FileAttributes.ReparsePoint) != 0))
            throw new InvalidDataException(
                "RSoP XML missing, oversized, or located through a reparse point.");
        try
        {
            using var stream = new FileStream(full, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.SequentialScan);
            if (stream.Length is < 64 or > MaxBytes)
                throw new InvalidDataException("RSoP file changed during import.");
            XDocument document;
            using (var reader = XmlReader.Create(stream, SafeXml))
                document = XDocument.Load(reader, LoadOptions.None);
            stream.Position = 0;
            string digest = Convert.ToHexString(SHA256.HashData(stream))
                .ToLowerInvariant();
            return (document, digest);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException(
                "Malformed or unsafe gpresult XML (DTDs/external entities are prohibited).", ex);
        }
    }

    private static XElement? Child(XElement element, string localName) =>
        element.Elements().SingleOrDefault(x => x.Name.LocalName == localName);

    internal static string? Text(XElement element, string localName) =>
        Child(element, localName)?.Value.Trim();

    internal static bool? Bool(XElement element, string localName)
    {
        var raw = Text(element, localName);
        if (raw is null) return null;
        if (bool.TryParse(raw, out bool result)) return result;
        throw new InvalidDataException("Malformed gpresult computer GPO flag: " + localName);
    }

    internal static string? GuidFromGpo(XElement item)
    {
        // The reported GUID is embedded in GPO/Identifier or in the
        // GPO LDAP Path. Never infer identity from display name.
        string? id = Text(item, "Identifier");
        string? path = Text(item, "Path");
        Guid parsedId = Guid.Empty;
        Guid parsedPath = Guid.Empty;
        if (id is not null)
        {
            if (!Guid.TryParse(id, out parsedId))
            {
                var matches = GuidPattern.Matches(id);
                if (matches.Count != 1 ||
                    !Guid.TryParse(matches[0].Groups["id"].Value, out parsedId))
                    parsedId = Guid.Empty;
            }
        }
        if (path is not null)
        {
            var matches = GuidPattern.Matches(path);
            if (matches.Count == 1 &&
                Guid.TryParse(matches[0].Groups["id"].Value, out var guid))
                parsedPath = guid;
        }
        // Two contradictory identifiers indicate a malformed imported report.
        if (parsedId != Guid.Empty && parsedPath != Guid.Empty &&
            parsedId != parsedPath)
            throw new InvalidDataException("Conflicting GPO identifiers in gpresult XML.");
        var result = parsedId != Guid.Empty ? parsedId : parsedPath;
        return result == Guid.Empty ? null : result.ToString("D");
    }

    internal static RsopEvidence ParseXml(string xml, string expectedComputer,
        string expectedDomain, DateTimeOffset now)
    {
        if (xml.Length is < 64 or > 12_000_000)
            throw new InvalidDataException("gpresult XML outside accepted size limits.");
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), SafeXml);
            return ParseDocument(XDocument.Load(reader, LoadOptions.None),
                expectedComputer, expectedDomain, now, "in-memory-fixture");
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException("Unsafe or malformed gpresult XML.", ex);
        }
    }

    private static RsopEvidence ParseDocument(XDocument document,
        string expectedComputer, string expectedDomain, DateTimeOffset now,
        string hash)
    {
        if (string.IsNullOrWhiteSpace(expectedComputer) ||
            string.IsNullOrWhiteSpace(expectedDomain) ||
            !expectedComputer.EndsWith("." + expectedDomain,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("No verified domain computer for RSoP import.");
        var root = document.Root;
        if (root is null || root.Name.LocalName != "Rsop" ||
            root.Name.NamespaceName != RsopNamespace)
            throw new InvalidDataException("Not a supported gpresult /x Rsop document.");
        var machine = root.Elements().Where(x =>
            x.Name.LocalName == "ComputerResults").ToArray();
        if (machine.Length != 1)
            throw new InvalidDataException("Missing or ambiguous computer-scope RSoP data.");
        if (Text(root, "DataType") is not "LoggedData")
            throw new InvalidDataException("Expected logged RSoP, not planning data.");

        string identity = Text(machine[0], "Name") ?? "";
        string reportDomain = Text(machine[0], "Domain") ?? "";
        if (reportDomain.Length > 0 &&
            !string.Equals(reportDomain, expectedDomain, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "RSoP computer domain conflicts with selected AD domain.");
        string shortName = expectedComputer.Split('.')[0];
        bool correctIdentity = string.Equals(identity, expectedComputer,
                StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(identity.TrimEnd('$'), shortName,
                 StringComparison.OrdinalIgnoreCase) &&
             string.Equals(reportDomain, expectedDomain,
                 StringComparison.OrdinalIgnoreCase)) ||
            (string.Equals(identity, expectedDomain + @"\" + shortName,
                 StringComparison.OrdinalIgnoreCase) &&
             string.Equals(reportDomain, expectedDomain,
                 StringComparison.OrdinalIgnoreCase));
        if (!correctIdentity)
            throw new InvalidDataException(
                "RSoP computer/domain do not match the verified selected computer.");

        string date = Text(root, "ReadTime") ?? "";
        if (!DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var readAt) ||
            (date.IndexOf('Z', StringComparison.OrdinalIgnoreCase) < 0 &&
             !Regex.IsMatch(date, @"[+-]\d{2}:\d{2}\z")) ||
            readAt > now.AddMinutes(5) || readAt.Year < 2015)
            throw new InvalidDataException("Missing, ambiguous or future RSoP read time.");
        var gpos = machine[0].Elements().Where(x =>
            x.Name.LocalName == "GPO").ToArray();
        if (gpos.Length > 512)
            throw new InvalidDataException("Too many computer-scope GPO entries.");
        var entries = new List<RsopGpoEvidence>();
        int missingGuids = 0;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in gpos)
        {
            string? guid = GuidFromGpo(item);
            if (guid is null) { missingGuids++; continue; }
            if (!used.Add(guid))
                throw new InvalidDataException(
                    "Duplicate GPO GUID in the imported computer RSoP list.");
            string name = Text(item, "Name") ?? "";
            if (name.Length > 256)
                throw new InvalidDataException("Oversized gpresult GPO name.");
            // GPResult ComputerResults/GPO emits these flags. A GPO appearing
            // in that collection alone is NOT evidence of application.
            bool? enabled = Bool(item, "Enabled");
            bool? valid = Bool(item, "IsValid");
            bool? filter = Bool(item, "FilterAllowed");
            bool? denied = Bool(item, "AccessDenied");
            string observation;
            string reason;
            if (enabled == false || valid == false || filter == false || denied == true)
            {
                observation = "Reported filtered";
                reason = enabled == false ? "Disabled" :
                    valid == false ? "Invalid GPO" :
                    denied == true ? "Access denied" : "Filtered by client";
            }
            else if (enabled == true && valid == true &&
                     filter == true && denied == false)
            {
                observation = "Reported applied";
                reason = "Computer RSoP flags permit application";
            }
            else
            {
                observation = "Insufficient flags";
                reason = "Missing computer RSoP flags; not proof of application";
            }
            entries.Add(new RsopGpoEvidence(guid, name, observation, reason));
        }
        return new RsopEvidence
        {
            Computer = expectedComputer, Domain = expectedDomain,
            ReadUtc = readAt.ToUniversalTime(), ImportedUtc = now,
            Sha256 = hash, Policies = entries.OrderBy(x => x.Name,
                StringComparer.OrdinalIgnoreCase).ToArray(),
            UnidentifiedEntries = missingGuids
        };
    }

    internal string ObservationFor(string gpoGuid)
    {
        if (!Guid.TryParse(gpoGuid, out var target)) return "Invalid GUID";
        var item = Policies.SingleOrDefault(p =>
            Guid.TryParse(p.Id, out var parsed) && parsed == target);
        return item?.Observation ?? "Not listed (unknown)";
    }

    internal string DetailFor(string gpoGuid)
    {
        if (!Guid.TryParse(gpoGuid, out var target)) return "Invalid GPO GUID";
        var entry = Policies.SingleOrDefault(p =>
            Guid.TryParse(p.Id, out var parsed) && parsed == target);
        return entry is null
            ? "GPO GUID absent from imported list; not evidence of non-application."
            : entry.Reason;
    }
}
