using System.Globalization;
using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Strict snapshot of the selected-DC AD-only GPO scope projection.
/// Independent from unsigned gpresult XML and from live endpoint behavior.
/// </summary>
internal sealed record AdScopedGpoLink(
    string Gpo, string GpoId, string Scope, string Origin, string LinkedAt,
    int Depth, int Order, bool Enforced, string State, string Security,
    string Wmi, string LoopbackValue);

internal sealed class DomainGpoScopeEvidence
{
    internal string Domain { get; private init; } = "";
    internal string Dc { get; private init; } = "";
    internal string Computer { get; private init; } = "";
    internal string ComputerOu { get; private init; } = "";
    internal string ActualComputerOu { get; private init; } = "";
    internal string RequestedUser { get; private init; } = "";
    internal string User { get; private init; } = "";
    internal string ActualUserOu { get; private init; } = "";
    internal string Loopback { get; private init; } = "";
    internal string ComputerToken { get; private init; } = "";
    internal string UserToken { get; private init; } = "";
    internal bool Complete { get; private init; }
    internal DateTimeOffset ObservedUtc { get; private init; }
    internal IReadOnlyList<AdScopedGpoLink> Links { get; private init; } = [];
    internal IReadOnlyList<string> Limitations { get; private init; } = [];
    internal bool ProvesWinningGpo => false;
    internal bool ProvesClientApplication => false;
    internal bool Fresh(DateTimeOffset now) =>
        ObservedUtc <= now.AddMinutes(2) &&
        now - ObservedUtc <= TimeSpan.FromMinutes(5);

    internal static DomainGpoScopeEvidence Parse(
        JsonElement data, DomainWideGpoReport report, string computer,
        string computerOu, string requestedUser, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (data.ValueKind != JsonValueKind.Object ||
            !Read(data, "domain", 253).Equals(report.Domain, StringComparison.OrdinalIgnoreCase) ||
            !NativeGpoLifecycleList.SameController(Read(data, "dc", 253), report.Dc) ||
            !Read(data, "computer", 253).Equals(computer, StringComparison.OrdinalIgnoreCase) ||
            !Read(data, "computerOu", 2048).Equals(computerOu, StringComparison.OrdinalIgnoreCase) ||
            !Read(data, "requestedUser", 256).Equals(requestedUser, StringComparison.OrdinalIgnoreCase) ||
            !NativeGpoConflictScan.SafeComputerName(computer, report.Domain) ||
            !report.ComputerOus.Contains(computerOu, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("AD applicability does not match this domain/DC/PC/user/OU.");
        string actualComputerOu = Read(data, "actualComputerOu", 2048);
        if (!NativeGpoConflictScan.Inside(actualComputerOu, computerOu))
            throw new InvalidDataException("AD computer object was not verified within selected OU.");
        string user = Read(data, "user", 256);
        string actualUserOu = Read(data, "actualUserOu", 2048);
        string loopback = Read(data, "loopback", 40);
        if (loopback is not ("Not configured" or "Merge candidate" or
            "Replace candidate" or "Unknown") ||
            (requestedUser.Length == 0 && (user.Length > 0 || actualUserOu.Length > 0)) ||
            (requestedUser.Length > 0 && (user.Length == 0 ||
                !actualUserOu.Contains("DC=", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("AD user or loopback scope is not verified.");
        string[] allowedToken = ["Read", "Unavailable", "Not selected"];
        string computerToken = Read(data, "computerToken", 30);
        string userToken = Read(data, "userToken", 30);
        if (!allowedToken.Contains(computerToken) ||
            !allowedToken.Contains(userToken))
            throw new InvalidDataException("Invalid AD group token state.");
        if (!data.TryGetProperty("observedUtc", out var stamp) ||
            stamp.ValueKind != JsonValueKind.String ||
            !stamp.TryGetDateTimeOffset(out var observed) ||
            observed.Year < 2020 || observed > now.AddMinutes(2))
            throw new InvalidDataException("Invalid/future AD scope timestamp.");
        if (!data.TryGetProperty("provesWinningGpo", out var winner) ||
            winner.ValueKind != JsonValueKind.False ||
            !data.TryGetProperty("provesClientApplication", out var applied) ||
            applied.ValueKind != JsonValueKind.False)
            throw new InvalidDataException("An AD-only projection cannot attest endpoint policy.");
        if (!data.TryGetProperty("complete", out var done) ||
            done.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Missing applicability completeness marker.");
        var array = data.GetProperty("links");
        var limitations = data.GetProperty("limitations");
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 750 ||
            limitations.ValueKind != JsonValueKind.Array ||
            limitations.GetArrayLength() > 15)
            throw new InvalidDataException("Unbounded AD scope result.");
        var values = new List<AdScopedGpoLink>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid AD GPO row.");
            string gpo = Read(item, "gpo", 150);
            string guid = Read(item, "gpoId", 40);
            string scope = Read(item, "scope", 10);
            string origin = Read(item, "origin", 50);
            string linkedAt = Read(item, "linkedAt", 2048);
            int depth = Int(item, "depth", 100);
            int order = Int(item, "order", 150);
            bool enforced = Bool(item, "enforced");
            string state = Read(item, "state", 65);
            string security = Read(item, "security", 35);
            string wmi = Read(item, "wmi", 35);
            string lb = Read(item, "loopbackValue", 10);
            if (!Guid.TryParseExact(guid, "D", out var id) || id == Guid.Empty ||
                scope is not ("Computer" or "User") ||
                origin is not ("Computer OU" or "User OU" or "Computer OU (loopback)") ||
                (scope == "Computer" && origin != "Computer OU") ||
                (scope == "User" && origin == "Computer OU") ||
                (origin == "Computer OU (loopback)" && requestedUser.Length == 0) ||
                (origin == "User OU" && requestedUser.Length == 0) ||
                !NativeGpoConflictScan.Inside(linkedAt,
                    "DC=" + report.Domain.Replace(".", ",DC=", StringComparison.OrdinalIgnoreCase)) ||
                state is not ("AD candidate" or "Conditional: WMI not evaluated" or
                    "Conditional: loopback not confirmed" or
                    "Conditional: loopback Replace candidate" or
                    "Conditional: loopback unknown" or
                    "Excluded: link disabled" or "Excluded: block inheritance" or
                    "Excluded: policy half disabled" or
                    "Excluded: matching security deny" or
                    "Unknown: security filtering" or
                    "Unknown: source unavailable" or
                    "Unknown: unreadable GPO/ACL") ||
                security is not ("Unknown" or "Read+Apply" or "Deny") ||
                wmi is not ("Unknown" or "Unverified" or "None") ||
                (scope == "User" && lb.Length > 0) ||
                (scope == "Computer" && lb is not ("" or "0" or "1" or "2")))
                throw new InvalidDataException("Unexpected AD link/scope/security classification.");
            values.Add(new(gpo, guid, scope, origin, linkedAt, depth, order,
                enforced, state, security, wmi, lb));
        }
        return new DomainGpoScopeEvidence
        {
            Domain = report.Domain, Dc = report.Dc, Computer = computer,
            ComputerOu = computerOu, ActualComputerOu = actualComputerOu,
            RequestedUser = requestedUser, User = user, ActualUserOu = actualUserOu,
            Loopback = loopback, ObservedUtc = observed.ToUniversalTime(),
            ComputerToken = computerToken, UserToken = userToken,
            Complete = done.GetBoolean(), Links = values,
            Limitations = limitations.EnumerateArray().Select(x =>
                x.ValueKind == JsonValueKind.String
                    ? (x.GetString() ?? "")[..Math.Min(x.GetString()?.Length ?? 0, 400)]
                    : throw new InvalidDataException("Invalid GPO limitation")).ToArray()
        };
    }

    private static string Read(JsonElement item, string name, int limit)
    {
        if (!item.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            value.GetString() is not string s || s.Length > limit ||
            s.Any(char.IsControl))
            throw new InvalidDataException("Invalid AD GPO property: " + name);
        return s;
    }

    private static int Int(JsonElement item, string name, int cap)
    {
        if (!item.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out int result) ||
            result < 0 || result > cap)
            throw new InvalidDataException("Invalid AD GPO link order/depth.");
        return result;
    }

    private static bool Bool(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Invalid AD GPO flag.");
        return value.GetBoolean();
    }
}
