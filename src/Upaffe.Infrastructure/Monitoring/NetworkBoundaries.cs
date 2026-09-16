using System.Net;
using System.Net.Sockets;

namespace Upaffe.Infrastructure.Monitoring;

public interface IHostResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemHostResolver : IHostResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken);
}

public interface IPinnedConnectionFactory
{
    ValueTask<Stream> ConnectAsync(
        IReadOnlyList<IPAddress> addresses,
        int port,
        CancellationToken cancellationToken);
}

public sealed class SocketPinnedConnectionFactory : IPinnedConnectionFactory
{
    public async ValueTask<Stream> ConnectAsync(
        IReadOnlyList<IPAddress> addresses,
        int port,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                lastFailure = exception;
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new IOException("No validated target address accepted a connection.", lastFailure);
    }
}

public static class PublicInternetAddress
{
    private sealed record Prefix(byte[] Network, int Bits, bool GloballyReachable);

    // Ordered by longest prefix at evaluation time. These exclusions and
    // exceptions follow the IANA IPv4 and IPv6 Special-Purpose Address
    // Registries; unclassified IPv6 is allowed only inside 2000::/3.
    private static readonly Prefix[] Ipv4Special =
    [
        V4("192.0.0.9", 32, true),
        V4("192.0.0.10", 32, true),
        V4("0.0.0.0", 8, false),
        V4("10.0.0.0", 8, false),
        V4("100.64.0.0", 10, false),
        V4("127.0.0.0", 8, false),
        V4("169.254.0.0", 16, false),
        V4("172.16.0.0", 12, false),
        V4("192.0.0.0", 24, false),
        V4("192.0.2.0", 24, false),
        V4("192.88.99.0", 24, false),
        V4("192.168.0.0", 16, false),
        V4("198.18.0.0", 15, false),
        V4("198.51.100.0", 24, false),
        V4("203.0.113.0", 24, false),
        V4("224.0.0.0", 4, false),
        V4("240.0.0.0", 4, false),
    ];

    private static readonly Prefix[] Ipv6Special =
    [
        V6("2001:1::1", 128, true),
        V6("2001:1::2", 128, true),
        V6("2001:1::3", 128, true),
        V6("2001:3::", 32, true),
        V6("2001:4:112::", 48, true),
        V6("2001:20::", 28, true),
        V6("2001:30::", 28, true),
        V6("64:ff9b::", 96, true),
        V6("::", 128, false),
        V6("::1", 128, false),
        V6("64:ff9b:1::", 48, false),
        V6("100::", 64, false),
        V6("100:0:0:1::", 64, false),
        V6("2001::", 23, false),
        V6("2001:db8::", 32, false),
        V6("2002::", 16, false),
        V6("3fff::", 20, false),
        V6("5f00::", 16, false),
        V6("fc00::", 7, false),
        V6("fe80::", 10, false),
        V6("ff00::", 8, false),
    ];

    public static bool IsAllowed(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            var special = Ipv4Special
                .Where(prefix => Contains(prefix, bytes))
                .OrderByDescending(prefix => prefix.Bits)
                .FirstOrDefault();
            return special?.GloballyReachable ?? true;
        }

        if (bytes.Length != 16)
        {
            return false;
        }

        if (Contains(V6("64:ff9b::", 96, true), bytes))
        {
            return IsAllowed(new IPAddress(bytes[^4..]));
        }

        var classified = Ipv6Special
            .Where(prefix => Contains(prefix, bytes))
            .OrderByDescending(prefix => prefix.Bits)
            .FirstOrDefault();
        if (classified is not null)
        {
            return classified.GloballyReachable;
        }

        return Contains(V6("2000::", 3, true), bytes);
    }

    private static bool Contains(Prefix prefix, byte[] address)
    {
        if (prefix.Network.Length != address.Length)
        {
            return false;
        }

        var wholeBytes = prefix.Bits / 8;
        for (var index = 0; index < wholeBytes; index++)
        {
            if (prefix.Network[index] != address[index])
            {
                return false;
            }
        }

        var remainingBits = prefix.Bits % 8;
        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xff << (8 - remainingBits));
        return (prefix.Network[wholeBytes] & mask) == (address[wholeBytes] & mask);
    }

    private static Prefix V4(string address, int bits, bool global) =>
        new(IPAddress.Parse(address).GetAddressBytes(), bits, global);

    private static Prefix V6(string address, int bits, bool global) =>
        new(IPAddress.Parse(address).GetAddressBytes(), bits, global);
}
