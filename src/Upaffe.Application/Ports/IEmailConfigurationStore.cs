namespace Upaffe.Application.Ports;

public sealed record EmailConfigurationSnapshot(
    long Version, string? Host, int? Port, string Security,
    string? SenderAddress, string? SenderName, string? PublicBaseUrl,
    string? Username, bool HasPassword, IReadOnlyList<string> DefaultRecipients,
    DateTimeOffset? UpdatedAt);

public sealed record SmtpConnectionSnapshot(
    string? Host, int? Port, string Security, string? SenderAddress,
    string? SenderName, string? PublicBaseUrl, string? Username, string? Password);

public interface IEmailConfigurationStore
{
    Task<EmailConfigurationSnapshot> ReadAsync(CancellationToken cancellationToken);
    Task<SmtpConnectionSnapshot> ReadConnectionAsync(CancellationToken cancellationToken);
    Task<EmailConfigurationSnapshot?> UpdateAsync(long expectedVersion, string? host,
        int? port, string security, string? senderAddress, string? senderName,
        string? publicBaseUrl, string? username, DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<EmailConfigurationSnapshot?> SetPasswordAsync(long expectedVersion, string? password,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<EmailConfigurationSnapshot?> SetDefaultRecipientsAsync(long expectedVersion,
        string[] recipients, DateTimeOffset now, CancellationToken cancellationToken);
}
