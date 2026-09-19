using System.Text.Json;
using System.Text.Json.Serialization;
using Upaffe.Api.Hosting;
using Upaffe.Api.Http;
using Upaffe.Application.Access;
using Upaffe.Application.Monitoring;
using Upaffe.Application.Notifications;
using Upaffe.Application.Ports;
using Upaffe.Application.Projects;
using Upaffe.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Secret reporting URLs must not be emitted by the framework's request-start log.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

var database = DatabaseSettings.FromConnectionString(
    builder.Configuration.GetConnectionString(DatabaseSettings.ConnectionStringName));
builder.Services.AddUpaffeInfrastructure(database);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ArmBootstrap>();
builder.Services.AddScoped<ReadBootstrapState>();
builder.Services.AddScoped<EstablishOperator>();
builder.Services.AddScoped<SignIn>();
builder.Services.AddScoped<SignOut>();
builder.Services.AddScoped<CreateManagementCredential>();
builder.Services.AddScoped<ListManagementCredentials>();
builder.Services.AddScoped<RotateManagementCredential>();
builder.Services.AddScoped<RevokeManagementCredential>();
builder.Services.AddScoped<CreateProject>();
builder.Services.AddScoped<ReadProject>();
builder.Services.AddScoped<ReadProjectReport>();
builder.Services.AddScoped<ListProjects>();
builder.Services.AddScoped<RenameProject>();
builder.Services.AddScoped<DeleteProject>();
builder.Services.AddScoped<RestoreProject>();
builder.Services.AddScoped<EmailConfigurationActs>();
builder.Services.AddScoped<TestEmail>();
builder.Services.AddScoped<RunEmailDelivery>();
builder.Services.AddScoped<MaintenanceActs>();
builder.Services.AddScoped<EmailStatusActs>();
builder.Services.AddScoped<PruneEmailHistory>();
builder.Services.AddScoped<CreateHttpMonitor>();
builder.Services.AddScoped<ReadHttpMonitor>();
builder.Services.AddScoped<ListHttpMonitors>();
builder.Services.AddScoped<UpdateHttpMonitor>();
builder.Services.AddScoped<PauseHttpMonitor>();
builder.Services.AddScoped<ResumeHttpMonitor>();
builder.Services.AddScoped<RemoveHttpMonitor>();
builder.Services.AddScoped<SetHttpMonitorHeader>();
builder.Services.AddScoped<RemoveHttpMonitorHeader>();
builder.Services.AddScoped<TestHttpMonitor>();
builder.Services.AddScoped<ListHttpCheckHistory>();
builder.Services.AddScoped<ListIncidentHistory>();
builder.Services.AddScoped<PruneHttpMonitorHistory>();
builder.Services.AddScoped<RunScheduledHttpCheck>();
builder.Services.AddScoped<RunPushDeadline>();
builder.Services.AddScoped<CreatePushMonitor>();
builder.Services.AddScoped<ReadPushMonitor>();
builder.Services.AddScoped<ListPushMonitors>();
builder.Services.AddScoped<UpdatePushMonitor>();
builder.Services.AddScoped<PausePushMonitor>();
builder.Services.AddScoped<ResumePushMonitor>();
builder.Services.AddScoped<RemovePushMonitor>();
builder.Services.AddScoped<ReadReportingCredential>();
builder.Services.AddScoped<IssueReportingCredential>();
builder.Services.AddScoped<RotateReportingCredential>();
builder.Services.AddScoped<RevokeReportingCredential>();
builder.Services.AddScoped<SubmitPushReport>();
builder.Services.AddScoped<SubmitSimplePushReport>();
builder.Services.AddScoped<ListPushReportHistory>();
builder.Services.AddScoped<ListPushIncidentHistory>();
builder.Services.AddScoped<PrunePushMonitorHistory>();
builder.Services.AddHostedService<SchemaMigrationService>();
builder.Services.AddHostedService<BootstrapService>();
builder.Services.AddHostedService<HttpMonitoringService>();
builder.Services.AddHostedService<PushMonitoringService>();
builder.Services.AddHostedService<EmailDeliveryService>();
builder.Services.AddHostedService<HttpHistoryRetentionService>();
builder.Services.AddHostedService<PushHistoryRetentionService>();
builder.Services.AddHostedService<EmailHistoryRetentionService>();
builder.Services.AddUpaffeOpenApi();
builder.Services.AddBrowserAuthentication();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

var app = builder.Build();

app.UseUpaffeVersion();
app.UseMiddleware<SecretPathRedactionMiddleware>();
app.UseMiddleware<ProblemMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseMiddleware<BrowserCsrfMiddleware>();
app.UseAuthorization();

app.MapOpenApi("/api/openapi/{documentName}.json").PublicAccess();

var api = app.MapGroup("/api");
api.MapInstance();
api.MapHealth();
api.MapBootstrap();
api.MapSession();
api.MapManagementCredentials();
api.MapProjects();
api.MapEmailConfiguration();
api.MapMaintenance();
api.MapHttpMonitors();
api.MapPushMonitors();
api.MapPushReports();
api.MapFallback(() => Results.NotFound()).PublicAccess();

app.MapFallbackToFile("index.html").PublicAccess();

app.Run();

/// <summary>Visible to the in-process host used by integration tests.</summary>
public partial class Program;
