using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;
using Upaffe.Domain.Notifications;
using Upaffe.Domain.Projects;

namespace Upaffe.Application.Notifications;

public sealed class EmailConfigurationActs(
    IEmailConfigurationStore settings, IProjectStore projects, TimeProvider clock)
{
    public Task<EmailConfigurationSnapshot> ReadAsync(Identity identity,
        CancellationToken cancellationToken)
    {
        _ = identity;
        return settings.ReadAsync(cancellationToken);
    }

    public async Task<EmailConfigurationSnapshot> UpdateAsync(Identity identity,
        long version, string? host, int? port, string? security, string? senderAddress,
        string? senderName, string? publicBaseUrl, string? username,
        CancellationToken cancellationToken)
    {
        _ = identity;
        ValidVersion(version);
        if (security is null) throw Validation("security", "SMTP security is required.");
        return await ValidatedAsync(async () => await settings.UpdateAsync(version, host, port,
            security, senderAddress, senderName, publicBaseUrl, username,
            clock.GetUtcNow(), cancellationToken), "settings");
    }

    public async Task<EmailConfigurationSnapshot> SetPasswordAsync(Identity identity,
        long version, string? password, CancellationToken cancellationToken)
    {
        _ = identity;
        ValidVersion(version);
        return await ValidatedAsync(async () => await settings.SetPasswordAsync(version,
            password, clock.GetUtcNow(), cancellationToken), "password");
    }

    public async Task<EmailConfigurationSnapshot> SetDefaultRecipientsAsync(Identity identity,
        long version, IReadOnlyList<string>? recipients, CancellationToken cancellationToken)
    {
        _ = identity;
        ValidVersion(version);
        var accepted = ValidRecipients(recipients);
        return await settings.SetDefaultRecipientsAsync(version, accepted,
            clock.GetUtcNow(), cancellationToken)
            ?? throw Refusal.Conflict("Email settings changed after the supplied version was read.");
    }

    public async Task<ProjectSnapshot> ReadProjectRecipientsAsync(Identity identity,
        string? key, CancellationToken cancellationToken)
    {
        _ = identity;
        var project = await projects.GetAsync(ValidKey(key), cancellationToken);
        return project ?? throw Refusal.NotFound("No such project.");
    }

    public async Task<ProjectSnapshot> SetProjectRecipientsAsync(Identity identity,
        string? key, long version, IReadOnlyList<string>? recipients,
        CancellationToken cancellationToken)
    {
        _ = identity;
        if (version <= 0) throw Validation("version", "A positive project version is required.");
        var result = await projects.ReplaceRecipientsAsync(ValidKey(key),
            ValidRecipients(recipients), version, clock.GetUtcNow(), cancellationToken);
        return result.Outcome switch
        {
            ProjectMutation.Changed when result.Project is not null => result.Project,
            ProjectMutation.Missing => throw Refusal.NotFound("No such project."),
            ProjectMutation.Deleted => throw Refusal.Conflict("A deleted project must be restored before it changes."),
            ProjectMutation.VersionConflict => throw Refusal.Conflict("The project changed after the supplied version was read."),
            _ => throw new InvalidOperationException("Invalid project recipient store result."),
        };
    }

    private static long ValidVersion(long version)
    {
        if (version < 0) throw Validation("version", "Email settings version must be nonnegative.");
        return version;
    }

    private static string ValidKey(string? key)
    {
        try { return Project.ValidateKey(key ?? string.Empty); }
        catch (ArgumentException ex) { throw Validation("key", ex.Message); }
    }

    private static string[] ValidRecipients(IReadOnlyList<string>? recipients)
    {
        try { return EmailConfiguration.NormalizeRecipients(recipients); }
        catch (ArgumentException ex) { throw Validation("recipients", ex.Message); }
    }

    private static async Task<EmailConfigurationSnapshot> ValidatedAsync(
        Func<Task<EmailConfigurationSnapshot?>> action, string field)
    {
        try
        {
            return await action() ?? throw Refusal.Conflict(
                "Email settings changed after the supplied version was read.");
        }
        catch (ArgumentException ex) { throw Validation(field, ex.Message); }
    }

    private static Refusal Validation(string field, string message) =>
        Refusal.Validation(new Dictionary<string, string[]> { [field] = [message] });
}
