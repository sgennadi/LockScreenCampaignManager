using System.Net.Sockets;

namespace LockScreenCampaignManager;

/// <summary>
/// Lightweight, read-only network reachability hint for verified AD endpoints.
/// TCP 135 is only an RPC endpoint-mapper indication. It is NOT proof that
/// Task Scheduler RPC, an installed LockScreen agent or a client lock screen
/// is healthy. NativeImmediateApply always performs separate authorization.
/// </summary>
internal static class AgentReachabilityProbe
{
    internal sealed record Result(string Host, bool RpcEndpointReachable, DateTimeOffset CheckedUtc);

    internal const int MaximumPerScan = 300;
    internal const int MaximumParallel = 12;
    internal const int ConnectionTimeoutMilliseconds = 1350;

    internal static async Task<IReadOnlyList<Result>> CheckAsync(
        IReadOnlyList<string> verifiedHosts, string domain,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifiedHosts);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        if (verifiedHosts.Count > MaximumPerScan)
            throw new ArgumentOutOfRangeException(nameof(verifiedHosts),
                "Limit connectivity checks to 300 explicitly listed AD computers.");
        if (verifiedHosts.Count != verifiedHosts.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new ArgumentException("Duplicate endpoint names are not allowed.", nameof(verifiedHosts));

        using var limiter = new SemaphoreSlim(MaximumParallel);
        var tasks = verifiedHosts.Select(async hostname =>
        {
            await limiter.WaitAsync(cancellationToken);
            try
            {
                if (!NativeImmediateActions.ValidHost(hostname, domain))
                    return new Result(hostname, false, DateTimeOffset.UtcNow);
                bool connected;
                using (var client = new TcpClient())
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(ConnectionTimeoutMilliseconds);
                    try
                    {
                        await client.ConnectAsync(hostname, 135, timeout.Token);
                        connected = client.Connected;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        connected = false;
                    }
                    catch (SocketException)
                    {
                        connected = false;
                    }
                    catch (IOException)
                    {
                        connected = false;
                    }
                }
                return new Result(hostname, connected, DateTimeOffset.UtcNow);
            }
            finally { limiter.Release(); }
        }).ToArray();
        return await Task.WhenAll(tasks);
    }
}
