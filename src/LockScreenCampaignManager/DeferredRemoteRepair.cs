using System.Text.Json;

namespace LockScreenCampaignManager;

/// <summary>
/// Shared, scope-validated RPC routine for foreground and scheduled background
/// retry. Every invocation derives a NEW Preview hash from AD/SYSVOL; queued
/// JSON never supplies authorization to bypass membership/GPO validation.
/// </summary>
internal static class DeferredRemoteRepair
{
    internal static (string Result, string Message) Execute(
        DeferredRepairItem item, DomainCredentials credentials)
    {
        if (credentials.IsAlternate && string.IsNullOrWhiteSpace(credentials.Password))
            throw new InvalidOperationException("Missing session-only alternate credentials.");

        var list = DomainPowerShellRunner.Execute(DomainOperation.Immediate, "List",
            new { domain = item.Domain, dc = item.DomainController,
                ouDn = item.OuDn, groupDn = item.GroupDn },
            credentials, 240_000).GetProperty("data");
        bool exists = list.EnumerateArray().Any(x =>
            string.Equals(x.GetProperty("name").GetString(), item.ComputerFqdn,
                StringComparison.OrdinalIgnoreCase));
        if (!exists) return ("invalid", "Computer is outside the approved OU / security group.");

        var preview = DomainPowerShellRunner.Execute(DomainOperation.Immediate, "Preview",
            new { domain = item.Domain, dc = item.DomainController,
                ouDn = item.OuDn, groupDn = item.GroupDn,
                gpoId = item.GpoId, computers = new[] { item.ComputerFqdn },
                purpose = "Repair" },
            credentials, 240_000).GetProperty("data");
        if (!string.Equals(preview.GetProperty("campaignId").GetString(),
                item.CampaignId, StringComparison.Ordinal) ||
            !string.Equals(preview.GetProperty("gpoId").GetString(),
                item.GpoId, StringComparison.OrdinalIgnoreCase))
            return ("invalid", "Managed GPO or campaign changed since approval.");

        string hash = preview.GetProperty("hash").GetString() ?? "";
        if (hash.Length != 64 || !hash.All(c => char.IsAsciiHexDigit(c)))
            return ("invalid", "Remote preview hash is invalid.");

        var applied = DomainPowerShellRunner.Execute(DomainOperation.Immediate, "Repair",
            new { domain = item.Domain, dc = item.DomainController,
                ouDn = item.OuDn, groupDn = item.GroupDn,
                gpoId = item.GpoId, computers = new[] { item.ComputerFqdn },
                purpose = "Repair", planHash = hash, approveGuid = item.GpoId },
            credentials, 150_000).GetProperty("data");
        var results = applied.GetProperty("results").EnumerateArray().ToArray();
        if (results.Length != 1 || results[0].ValueKind != JsonValueKind.Object ||
            !string.Equals(results[0].GetProperty("host").GetString(), item.ComputerFqdn,
                StringComparison.OrdinalIgnoreCase))
            return ("invalid", "Unexpected remote host/result.");
        string status = results[0].GetProperty("status").GetString() ?? "failed";
        return status is "success" or "pending" or "offline" ? (status, status)
            : ("failed", "Task Scheduler or agent returned a non-success result.");
    }
}
