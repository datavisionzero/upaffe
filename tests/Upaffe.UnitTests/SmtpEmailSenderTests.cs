using System.Net;
using System.Net.Sockets;
using Upaffe.Application.Ports;
using Upaffe.Infrastructure.Notifications;

namespace Upaffe.UnitTests;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task A_silent_relay_hits_the_bounded_deadline_without_exposing_its_address()
    {
        var ct = TestContext.Current.CancellationToken;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accept = listener.AcceptTcpClientAsync(ct);
            var sender = new SmtpEmailSender(TimeSpan.FromMilliseconds(200));
            var result = await sender.SendAsync(
                new SmtpConnectionSnapshot("127.0.0.1", port, "none", "from@example.test",
                    null, null, null, null),
                new RenderedEmail("to@example.test", "test", "test", "<p>test</p>",
                    "test@upaffe.invalid"), ct);
            using var ignored = await accept;
            Assert.False(result.AcceptedBySmtp);
            Assert.True(result.Transient);
            Assert.Equal("smtp_timeout", result.FailureCode);
        }
        finally { listener.Stop(); }
    }
}
