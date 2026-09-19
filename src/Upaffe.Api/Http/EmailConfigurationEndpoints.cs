using System.Text.Json.Serialization;
using System.Globalization;
using Upaffe.Application.Notifications;
using Upaffe.Application.Ports;

namespace Upaffe.Api.Http;

public sealed record UpdateEmailSettingsRequest(
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version,
    string? Host, int? Port, string? Security, string? SenderAddress,
    string? SenderName, string? PublicBaseUrl, string? Username);
public sealed record SetSmtpPasswordRequest(
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version,
    string? Password);
public sealed record ReplaceRecipientsRequest(
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version,
    IReadOnlyList<string>? Recipients);
public sealed record ProjectRecipientsResponse(
    string ProjectKey,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version,
    IReadOnlyList<string> Recipients);

public static class EmailConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapEmailConfiguration(this IEndpointRouteBuilder endpoints)
    {
        var email = endpoints.MapGroup("/email").ManagementAccess();
        email.MapGet("/settings", async (HttpContext http, EmailConfigurationActs acts,
                CancellationToken cancellationToken) =>
            await acts.ReadAsync(http.ActingIdentity(), cancellationToken))
            .WithName("ReadEmailSettings").WithSummary("Read safe instance SMTP settings and default recipients.")
            .Produces<EmailConfigurationSnapshot>();

        email.MapPut("/settings", async (UpdateEmailSettingsRequest request, HttpContext http,
                EmailConfigurationActs acts, CancellationToken cancellationToken) =>
            await acts.UpdateAsync(http.ActingIdentity(), request.Version, request.Host,
                request.Port, request.Security, request.SenderAddress, request.SenderName,
                request.PublicBaseUrl, request.Username, cancellationToken))
            .WithName("UpdateEmailSettings").WithSummary("Update SMTP settings at the read version; credentials are separate.")
            .Produces<EmailConfigurationSnapshot>().Produces<ProblemResponse>(400, "application/problem+json")
            .Produces<ProblemResponse>(409, "application/problem+json");

        email.MapPut("/password", async (SetSmtpPasswordRequest request, HttpContext http,
                EmailConfigurationActs acts, CancellationToken cancellationToken) =>
            await acts.SetPasswordAsync(http.ActingIdentity(), request.Version,
                request.Password, cancellationToken))
            .WithName("ReplaceSmtpPassword").WithSummary("Replace the write-only SMTP password.")
            .Produces<EmailConfigurationSnapshot>().Produces<ProblemResponse>(400, "application/problem+json")
            .Produces<ProblemResponse>(409, "application/problem+json");

        email.MapDelete("/password", async (HttpContext http,
                EmailConfigurationActs acts, CancellationToken cancellationToken,
                string version = "") =>
            await acts.SetPasswordAsync(http.ActingIdentity(), ParsedVersion(version),
                null, cancellationToken))
            .WithName("ClearSmtpPassword").WithSummary("Clear the SMTP password explicitly.")
            .Produces<EmailConfigurationSnapshot>().Produces<ProblemResponse>(409, "application/problem+json");

        email.MapPut("/default-recipients", async (ReplaceRecipientsRequest request,
                HttpContext http, EmailConfigurationActs acts, CancellationToken cancellationToken) =>
            await acts.SetDefaultRecipientsAsync(http.ActingIdentity(), request.Version,
                request.Recipients, cancellationToken))
            .WithName("ReplaceDefaultRecipients").WithSummary("Replace recipients copied into future projects.")
            .Produces<EmailConfigurationSnapshot>().Produces<ProblemResponse>(400, "application/problem+json")
            .Produces<ProblemResponse>(409, "application/problem+json");

        var projects = endpoints.MapGroup("/projects/{key}/recipients").ManagementAccess();
        projects.MapGet(string.Empty, async (string key, HttpContext http,
                EmailConfigurationActs acts, CancellationToken cancellationToken) =>
            ProjectResponse(await acts.ReadProjectRecipientsAsync(http.ActingIdentity(),
                key, cancellationToken)))
            .WithName("ReadProjectRecipients").WithSummary("Read a project's current recipients and version.")
            .Produces<ProjectRecipientsResponse>().Produces<ProblemResponse>(404, "application/problem+json");

        projects.MapPut(string.Empty, async (string key, ReplaceRecipientsRequest request,
                HttpContext http, EmailConfigurationActs acts, CancellationToken cancellationToken) =>
            ProjectResponse(await acts.SetProjectRecipientsAsync(http.ActingIdentity(),
                key, request.Version, request.Recipients, cancellationToken)))
            .WithName("ReplaceProjectRecipients").WithSummary("Replace live project recipients at its read version.")
            .Produces<ProjectRecipientsResponse>().Produces<ProblemResponse>(400, "application/problem+json")
            .Produces<ProblemResponse>(404, "application/problem+json")
            .Produces<ProblemResponse>(409, "application/problem+json");
        return endpoints;
    }

    private static ProjectRecipientsResponse ProjectResponse(ProjectSnapshot value) =>
        new(value.Key, value.Version, value.Recipients);

    private static long ParsedVersion(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : -1;
}
