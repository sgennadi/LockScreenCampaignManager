using System.Security.Cryptography;
using System.Text;

namespace LockScreenCampaignManager;

/// <summary>
/// Pure mirror of the client agent's stable SHA-256 ordering, for preview
/// and offline regression testing. No RPC, AD lookup or machine secrets.
/// The returned numbers are zero-based physical image indices assigned
/// to chronological schedule slots.
/// </summary>
internal static class CampaignDeviceImageOrder
{
    internal static IReadOnlyList<int> Resolve(string campaignId,
        string computerName, int count, bool randomize)
    {
        if (count is < 0 or > CampaignSchedule.MaxImages ||
            string.IsNullOrWhiteSpace(campaignId))
            throw new InvalidDataException("Invalid image permutation inputs.");
        if (!randomize) return Enumerable.Range(0, count).ToArray();
        if (string.IsNullOrWhiteSpace(computerName) ||
            computerName.Length > 63 ||
            computerName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new InvalidDataException("Enter a valid short computer name.");
        return Enumerable.Range(0, count)
            .Select(i => {
                string name = $"{i + 1:D2}.jpg";
                string key = campaignId.ToLowerInvariant() + "|" +
                    computerName.ToUpperInvariant() + "|" + name;
                byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
                return (Index: i, Hash: Convert.ToHexString(digest));
            })
            .OrderBy(x => x.Hash, StringComparer.Ordinal)
            .ThenBy(x => x.Index)
            .Select(x => x.Index)
            .ToArray();
    }
}
