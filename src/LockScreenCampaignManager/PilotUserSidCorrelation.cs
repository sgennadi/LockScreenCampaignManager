using System.Security.Principal;

namespace LockScreenCampaignManager;

/// <summary>
/// Conservative SID comparison between a selected AD LDAP user object and
/// optional UNSIGNED locally captured HKCU evidence. Matching values can
/// correlate names; they never authenticate the submitted report or prove
/// that a policy applied in the interactive session.
/// </summary>
internal static class PilotUserSidCorrelation
{
    internal static bool ValidDomainAccountSid(string? sid)
    {
        if (string.IsNullOrEmpty(sid) || sid.Length > 184 ||
            !sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) ||
            sid.Split('-').Length != 8)
            return false;
        try
        {
            return new SecurityIdentifier(sid).Value == sid;
        }
        catch (ArgumentException) { return false; }
        catch (SystemException) { return false; }
    }

    internal static bool Matches(string? adSid, string? endpointSid) =>
        ValidDomainAccountSid(adSid) &&
        ValidDomainAccountSid(endpointSid) &&
        string.Equals(adSid, endpointSid, StringComparison.Ordinal);
}
