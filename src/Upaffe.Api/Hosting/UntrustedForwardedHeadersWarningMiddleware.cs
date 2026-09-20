namespace Upaffe.Api.Hosting;

/// <summary>Reports a configured proxy address that no longer matches the connecting peer.</summary>
public sealed class UntrustedForwardedHeadersWarningMiddleware(
    RequestDelegate next,
    TrustedProxySettings settings,
    TimeProvider clock,
    ILogger<UntrustedForwardedHeadersWarningMiddleware> logger)
{
    private static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);
    private readonly object gate = new();
    private DateTimeOffset nextWarning;

    public Task InvokeAsync(HttpContext context)
    {
        var peer = context.Connection.RemoteIpAddress;
        var address = peer?.IsIPv4MappedToIPv6 == true ? peer.MapToIPv4() : peer;
        var trusted = peer is not null
            && (settings.ProxyAddresses.Contains(peer)
                || (address is not null && settings.ProxyAddresses.Contains(address)));
        if (address is not null
            && HasForwardedHeaders(context.Request.Headers)
            && !trusted
            && ShouldWarn(clock.GetUtcNow()))
        {
            logger.LogWarning(
                "Ignored forwarded headers from untrusted peer {PeerAddress}; check {Setting}.",
                address, TrustedProxySettings.ProxyAddressesVariable);
        }

        return next(context);
    }

    private static bool HasForwardedHeaders(IHeaderDictionary headers) =>
        headers.ContainsKey("X-Forwarded-For")
        || headers.ContainsKey("X-Forwarded-Proto")
        || headers.ContainsKey("X-Forwarded-Host");

    private bool ShouldWarn(DateTimeOffset now)
    {
        lock (gate)
        {
            if (now < nextWarning)
            {
                return false;
            }

            nextWarning = now.Add(WarningInterval);
            return true;
        }
    }
}
