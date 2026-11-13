using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Strict read-only all-domain GPC inventory. Each row is a configured source
/// candidate, NEVER winning RSoP or proof of applicability to a user/device.
/// </summary>
internal sealed record DomainGpoMatch(
    string Gpo, string GpoId, string Scope, string Source, string Key,
    string Setting, string Value, string Risk, string Reason,
    string LinkStatus, string LinkedAt, string Applicability);

internal sealed record DomainGpoIssue(string Gpo, string GpoId,
    string Scope, string Reason);

internal sealed class DomainWideGpoReport
{
    internal string Domain { get; private init; } = "";
    internal string Dc { get; private init; } = "";
    internal DateTimeOffset ObservedUtc { get; private init; }
    internal int GpoCount { get; private init; }
    internal int InspectedHalves { get; private init; }
    internal int EmptyHalves { get; private init; }
    internal bool RecognizedSourcesReadable { get; private init; }
    internal IReadOnlyList<string> ComputerOus { get; private init; } = [];
    internal IReadOnlyList<string> UserOus { get; private init; } = [];
    internal IReadOnlyList<DomainGpoMatch> Matches { get; private init; } = [];
    internal IReadOnlyList<DomainGpoIssue> Issues { get; private init; } = [];
    internal IReadOnlyList<string> Notes { get; private init; } = [];
    internal int HighRisk => Matches.Count(x => x.Risk == "High");
    internal int Warnings => Matches.Count(x => x.Risk == "Warning");
    internal int LinkedCandidates => Matches.Count(x => x.LinkStatus == "Linked candidate");
    internal int Count => Matches.Count;

    internal static DomainWideGpoReport Parse(
        JsonElement data, string expectedDomain, string expectedDc,
        IReadOnlyCollection<string> expectedComputerOus,
        IReadOnlyCollection<string> expectedUserOus)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !Read(data, "domain", 253).Equals(expectedDomain, StringComparison.OrdinalIgnoreCase) ||
            !NativeGpoLifecycleList.SameController(Read(data, "dc", 253), expectedDc))
            throw new InvalidDataException("Domain GPO evidence is not scoped to selected DC.");
        var stamp = data.GetProperty("observedUtc");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (stamp.ValueKind != JsonValueKind.String ||
            !stamp.TryGetDateTimeOffset(out DateTimeOffset observed) ||
            observed.Year < 2020 ||
            observed > now.AddMinutes(2) ||
            now - observed > TimeSpan.FromMinutes(5))
            throw new InvalidDataException("GPO scan evidence timestamp is not fresh.");
        static string[] ParseOus(JsonElement data, string name,
            IReadOnlyCollection<string> expected)
        {
            var array = data.GetProperty(name);
            if (array.ValueKind != JsonValueKind.Array ||
                array.GetArrayLength() > 25)
                throw new InvalidDataException("Unbounded GPO OU evidence.");
            var actual = array.EnumerateArray().Select(v =>
                v.ValueKind == JsonValueKind.String
                    ? v.GetString() ?? "" : "").ToArray();
            if (actual.Any(x => x.Length is < 4 or > 2048 ||
                x.Any(char.IsControl) ||
                !x.StartsWith("OU=", StringComparison.OrdinalIgnoreCase)) ||
                actual.Length != actual.Distinct(StringComparer.OrdinalIgnoreCase).Count() ||
                !actual.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    .SetEquals(expected))
                throw new InvalidDataException("GPO OU evidence does not match selected scope.");
            return actual;
        }
        var computers = ParseOus(data, "computerOus", expectedComputerOus);
        var users = ParseOus(data, "userOus", expectedUserOus);
        if (computers.Length == 0 && users.Length == 0)
            throw new InvalidDataException("No OU was verified for GPO relevance.");
        static int Count(JsonElement json, string name, int max)
        {
            int value = json.GetProperty(name).GetInt32();
            if (value < 0 || value > max)
                throw new InvalidDataException("Invalid GPO scan count: " + name);
            return value;
        }
        int gpos = Count(data, "gpoCount", 2000);
        int inspected = Count(data, "inspectedHalves", 4000);
        int empty = Count(data, "emptyHalves", 4000);
        if (inspected + empty > gpos * 2)
            throw new InvalidDataException("GPO policy halves exceed GPC count.");
        var matches = data.GetProperty("matches");
        var issues = data.GetProperty("issues");
        var notes = data.GetProperty("notes");
        if (matches.ValueKind != JsonValueKind.Array || matches.GetArrayLength() > 6000 ||
            issues.ValueKind != JsonValueKind.Array || issues.GetArrayLength() > 300 ||
            notes.ValueKind != JsonValueKind.Array || notes.GetArrayLength() > 100)
            throw new InvalidDataException("Domain GPO result exceeds safety bounds.");

        var rows = matches.EnumerateArray().Select(v =>
            new DomainGpoMatch(Read(v, "gpo", 150),
                Read(v, "gpoId", 40), Read(v, "scope", 20),
                Read(v, "source", 90), Read(v, "key", 600),
                Read(v, "setting", 120), Read(v, "value", 470),
                Read(v, "risk", 40), Read(v, "reason", 400),
                Read(v, "linkStatus", 48), Read(v, "linkedAt", 2048),
                Read(v, "applicability", 550))).ToArray();
        foreach (var row in rows)
        {
            if (!Guid.TryParseExact(row.GpoId, "D", out Guid id) || id == Guid.Empty ||
                row.Scope is not ("Computer" or "User") ||
                row.Source is not ("Registry.pol" or
                    @"Preferences\Registry\Registry.xml" or
                    @"Preferences\PowerOptions\PowerOptions.xml" or
                    @"Microsoft\Windows NT\SecEdit\GptTmpl.inf") ||
                row.Risk is not ("High" or "Warning" or "Info" or "Inactive/filtered") ||
                row.LinkStatus is not ("Linked candidate" or "Outside selected OUs" or
                    "Inactive/filtered" or "User OU not verified") ||
                (row.Risk is "High" or "Warning") && row.LinkStatus != "Linked candidate")
                throw new InvalidDataException("Invalid all-GPO source or applicability field.");
        }
        var errors = issues.EnumerateArray().Select(v =>
            new DomainGpoIssue(Read(v, "gpo", 150),
                Read(v, "gpoId", 40), Read(v, "scope", 20),
                Read(v, "reason", 480))).ToArray();
        if (errors.Any(v => !Guid.TryParseExact(v.GpoId, "D", out _) ||
            v.Scope is not ("User" or "Computer" or "Both")))
            throw new InvalidDataException("Invalid GPO source issue evidence.");
        var remarks = notes.EnumerateArray().Select(v =>
            v.ValueKind == JsonValueKind.String
                ? ReadString(v, 600) : throw new InvalidDataException("Invalid GPO note."))
            .ToArray();

        bool complete = data.GetProperty("complete").GetBoolean();
        if (complete != (errors.Length == 0) ||
            Count(data, "count", 6000) != rows.Length ||
            Count(data, "issueCount", 300) != errors.Length ||
            Count(data, "linkedCandidates", 6000) !=
                rows.Count(x => x.LinkStatus == "Linked candidate") ||
            Count(data, "highRisk", 6000) != rows.Count(x => x.Risk == "High") ||
            Count(data, "warningsCount", 6000) != rows.Count(x => x.Risk == "Warning"))
            throw new InvalidDataException("Domain GPO counts/completeness do not match evidence.");
        if (rows.Select(x => x.GpoId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > gpos)
            throw new InvalidDataException("Domain GPO matches exceed inventoried GPCs.");

        return new DomainWideGpoReport {
            Domain = expectedDomain, Dc = Read(data, "dc", 253),
            ObservedUtc = observed.ToUniversalTime(),
            GpoCount = gpos, InspectedHalves = inspected, EmptyHalves = empty,
            RecognizedSourcesReadable = complete,
            ComputerOus = computers, UserOus = users, Matches = rows,
            Issues = errors, Notes = remarks
        };
    }

    private static string Read(JsonElement data, string name, int max) =>
        ReadString(data.GetProperty(name), max);

    private static string ReadString(JsonElement value, int max)
    {
        if (value.ValueKind != JsonValueKind.String ||
            value.GetString() is not string text ||
            text.Length > max || text.Any(char.IsControl))
            throw new InvalidDataException("Invalid untrusted GPO evidence string.");
        return text;
    }

    internal static string BuildCsv(IEnumerable<DomainGpoMatch> matches)
    {
        static string Escape(string? value)
        {
            string safe = (value ?? "").Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);
            if (safe.Length > 0 && "=+-@".Contains(safe[0]))
                safe = "'" + safe;
            return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }
        var output = new StringBuilder(
            "GPO,GPO ID,Scope,Source,Key,Setting,Value,Risk,Link status,Linked at,Reason,Applicability\r\n");
        foreach (var row in matches)
        {
            string[] fields = [
                row.Gpo, row.GpoId, row.Scope, row.Source, row.Key, row.Setting,
                row.Value, row.Risk, row.LinkStatus, row.LinkedAt,
                row.Reason, row.Applicability
            ];
            output.Append(string.Join(",", fields.Select(Escape))).Append("\r\n");
        }
        return output.ToString();
    }
}
