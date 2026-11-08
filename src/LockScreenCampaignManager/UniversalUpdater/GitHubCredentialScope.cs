using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace UniversalUpdater;

/// <summary>
/// Scope an optional, user-supplied GitHub read-only session token to exactly
/// the configured GitHub API. NEVER attach it to github.com, release CDN,
/// arbitrary redirects, logs or state files. This is not a credential vault.
/// </summary>
public static class GitHubCredentialScope
{
    private static readonly Regex TokenSyntax = new(
        @"\A[A-Za-z0-9_-]{12,512}\z", RegexOptions.CultureInvariant);

    public static string? ValidateSessionToken(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        // No trimming: silently normalizing copied credentials can hide
        // paste mistakes and result in misleading anonymous behavior.
        if (!TokenSyntax.IsMatch(value))
            throw new ArgumentException(
                "The session token contains invalid characters or has an unexpected length.");
        return value;
    }

    public static Uri AssetApiUri(string repository, long assetId)
    {
        if (assetId <= 0 ||
            !Regex.IsMatch(repository, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z",
                RegexOptions.CultureInvariant))
            throw new InvalidDataException("Invalid pinned GitHub release asset identity.");
        return new Uri($"https://api.github.com/repos/{repository}/releases/assets/{assetId}");
    }

    public static bool IsMetadataEndpoint(Uri url, UpdateOptions options)
    {
        if (!IsPinnedApiHost(url)) return false;
        var expected = $"/repos/{options.Repository}/releases/";
        return options.Channel == UpdateChannel.Stable
            ? url.AbsolutePath == expected + "latest" && url.Query.Length == 0
            : url.AbsolutePath == expected && url.Query == "?per_page=100";
    }

    public static bool IsAssetEndpoint(Uri url, string repository, long assetId) =>
        IsPinnedApiHost(url) &&
        assetId > 0 &&
        url.AbsolutePath == $"/repos/{repository}/releases/assets/{assetId}" &&
        url.Query.Length == 0;

    private static bool IsPinnedApiHost(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort &&
        url.Host == "api.github.com" && url.UserInfo.Length == 0 &&
        url.Fragment.Length == 0;

    internal static void AttachIfAllowed(
        HttpRequestMessage request, string? sessionToken, bool authorized)
    {
        if (sessionToken is null) return;
        if (!authorized) throw new InvalidDataException(
            "Refusing to forward a GitHub session token outside its pinned API endpoint.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
    }
}
