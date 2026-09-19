using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;
using Upaffe.Domain.Notifications;

namespace Upaffe.Application.Notifications;

public sealed record TestEmailResult(string Status, DateTimeOffset AcceptedAt);

public sealed class TestEmail(IEmailConfigurationStore settings, IEmailSender sender,
    TimeProvider clock)
{
    public async Task<TestEmailResult> ExecuteAsync(Identity identity, string? recipient,
        CancellationToken cancellationToken)
    {
        _ = identity;
        string address;
        try { address = EmailConfiguration.NormalizeAddress(recipient ?? string.Empty); }
        catch (ArgumentException ex)
        {
            throw Refusal.Validation(new Dictionary<string, string[]>
            { ["recipient"] = [ex.Message] });
        }
        var connection = await settings.ReadConnectionAsync(cancellationToken);
        if (connection.Host is null || connection.Port is null || connection.SenderAddress is null
            || (connection.Username is not null && connection.Password is null))
            throw Refusal.EmailNotConfigured();
        var result = await sender.SendAsync(connection,
            IncidentEmailRenderer.Test(address, clock.GetUtcNow()), cancellationToken);
        if (!result.AcceptedBySmtp)
            throw Refusal.SmtpRejected(result.FailureCode ?? "smtp_failed");
        return new("accepted_by_smtp", clock.GetUtcNow());
    }
}
