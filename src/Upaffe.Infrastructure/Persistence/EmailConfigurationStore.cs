using Microsoft.EntityFrameworkCore;
using Npgsql;
using Upaffe.Application.Ports;
using Upaffe.Domain.Notifications;

namespace Upaffe.Infrastructure.Persistence;

internal sealed class EmailConfigurationStore(UpaffeDbContext context) : IEmailConfigurationStore
{
    public async Task<EmailConfigurationSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var value = await context.EmailConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return value is null ? Empty() : Safe(value);
    }

    public async Task<SmtpConnectionSnapshot> ReadConnectionAsync(CancellationToken cancellationToken)
    {
        var value = await context.EmailConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return value is null ? new(null, null, "starttls", null, null, null, null, null)
            : new(value.Host, value.Port, value.Security, value.SenderAddress,
                value.SenderName, value.PublicBaseUrl, value.Username, value.Password);
    }

    public Task<EmailConfigurationSnapshot?> UpdateAsync(long expectedVersion, string? host,
        int? port, string security, string? senderAddress, string? senderName,
        string? publicBaseUrl, string? username, DateTimeOffset now,
        CancellationToken cancellationToken) => MutateAsync(expectedVersion,
            value => value.Update(host, port, security, senderAddress, senderName,
                publicBaseUrl, username, now), now, cancellationToken);

    public Task<EmailConfigurationSnapshot?> SetPasswordAsync(long expectedVersion,
        string? password, DateTimeOffset now, CancellationToken cancellationToken) =>
        MutateAsync(expectedVersion, value =>
        {
            if (password is null) value.ClearPassword(now);
            else value.SetPassword(password, now);
        }, now, cancellationToken);

    public Task<EmailConfigurationSnapshot?> SetDefaultRecipientsAsync(long expectedVersion,
        string[] recipients, DateTimeOffset now, CancellationToken cancellationToken) =>
        MutateAsync(expectedVersion, value => value.SetDefaultRecipients(recipients, now),
            now, cancellationToken);

    private async Task<EmailConfigurationSnapshot?> MutateAsync(long expectedVersion,
        Action<EmailConfiguration> mutation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var value = await context.EmailConfigurations.SingleOrDefaultAsync(cancellationToken);
        if (value is null)
        {
            if (expectedVersion != 0) return null;
            value = EmailConfiguration.Create(now);
            context.EmailConfigurations.Add(value);
        }
        else if (value.Version != expectedVersion) return null;
        mutation(value);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Safe(value);
        }
        catch (DbUpdateConcurrencyException) { return null; }
        catch (DbUpdateException ex) when (expectedVersion == 0
            && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return null; }
    }

    private static EmailConfigurationSnapshot Safe(EmailConfiguration value) =>
        new(value.Version, value.Host, value.Port, value.Security, value.SenderAddress,
            value.SenderName, value.PublicBaseUrl, value.Username, value.Password is not null,
            value.DefaultRecipients, value.UpdatedAt);

    private static EmailConfigurationSnapshot Empty() =>
        new(0, null, null, "starttls", null, null, null, null, false, [], null);
}
