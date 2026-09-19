using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Upaffe.Api.Hosting;

/// <summary>Limits forwarded request identity to explicitly named reverse proxies.</summary>
public sealed record TrustedProxySettings(Uri PublicOrigin, IReadOnlyList<IPAddress> ProxyAddresses)
{
    public const string ProxyAddressesVariable = "UPAFFE_TRUSTED_PROXY_IPS";
    public const string PublicOriginVariable = "UPAFFE_PUBLIC_ORIGIN";

    public static TrustedProxySettings? Read(IConfiguration configuration)
    {
        var addressesText = configuration[ProxyAddressesVariable];
        var originText = configuration[PublicOriginVariable];
        var hasAddresses = !string.IsNullOrWhiteSpace(addressesText);
        var hasOrigin = !string.IsNullOrWhiteSpace(originText);
        if (!hasAddresses && !hasOrigin)
        {
            return null;
        }

        if (!hasAddresses || !hasOrigin)
        {
            throw new InvalidOperationException(
                $"Configure both {ProxyAddressesVariable} and {PublicOriginVariable} for HTTPS proxy operation.");
        }

        if (!Uri.TryCreate(originText, UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(origin.Host)
            || origin.UserInfo.Length != 0
            || origin.AbsolutePath != "/"
            || origin.Query.Length != 0
            || origin.Fragment.Length != 0)
        {
            throw new InvalidOperationException(
                $"{PublicOriginVariable} must be an HTTPS origin without a path, query, or fragment.");
        }

        var parts = addressesText!.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 8
            || parts.Any(part => !IPAddress.TryParse(part, out var parsed)
                || parsed.Equals(IPAddress.Any)
                || parsed.Equals(IPAddress.IPv6Any)))
        {
            throw new InvalidOperationException(
                $"{ProxyAddressesVariable} must list 1-8 exact proxy IP addresses.");
        }

        var addresses = parts.Select(IPAddress.Parse).Distinct().ToArray();
        return new TrustedProxySettings(origin, addresses);
    }

    public ForwardedHeadersOptions ForwardedHeadersOptions()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost,
            ForwardLimit = 1,
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var address in ProxyAddresses)
        {
            options.KnownProxies.Add(address);
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                options.KnownProxies.Add(address.MapToIPv6());
            }
        }

        options.AllowedHosts.Add(PublicOrigin.Host);
        return options;
    }
}
