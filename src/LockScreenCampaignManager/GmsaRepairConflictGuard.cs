namespace LockScreenCampaignManager;

/// <summary>
/// Pure duplicate-RPC guard for foreground Repair. A previously submitted
/// remote task can exist even after the gMSA worker is disabled or removed.
/// A Queued request may only be superseded by ReserveApproved while worker
/// configuration is enabled and protected. Never change the queue here.
/// </summary>
internal static class GmsaRepairConflictGuard
{
    internal static IReadOnlyList<string> BlockedHosts(
        IEnumerable<DeferredRepairItem> existing, string campaignId,
        string gpoId, IEnumerable<string> selectedFqdn, bool workerEnabled)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var selected = new HashSet<string>(selectedFqdn, StringComparer.OrdinalIgnoreCase);
        if (!Guid.TryParse(gpoId, out var guid))
            throw new InvalidDataException("Cannot check gMSA Repair without a valid GPO GUID.");
        return existing.Where(x => x.CampaignId == campaignId &&
                Guid.TryParse(x.GpoId, out var id) && id == guid &&
                selected.Contains(x.ComputerFqdn) &&
                (x.State is "Attempting" or "AwaitingHealth" or "AwaitingTask" ||
                 !workerEnabled && x.State == "Queued"))
            .Select(x => x.ComputerFqdn)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).Take(5).ToArray();
    }
}
