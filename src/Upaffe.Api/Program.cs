using System.Text.Json;
using System.Text.Json.Serialization;
using Upaffe.Api.Hosting;
using Upaffe.Api.Http;
using Upaffe.Application.Access;
using Upaffe.Application.Ports;
using Upaffe.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHostedService<SchemaMigrationService>();
builder.Services.AddHostedService<BootstrapService>();
builder.Services.AddUpaffeOpenApi();
builder.Services.AddBrowserAuthentication();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.UseUpaffeVersion();
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
api.MapFallback(() => Results.NotFound()).PublicAccess();

app.MapFallbackToFile("index.html").PublicAccess();

app.Run();

/// <summary>Visible to the in-process host used by integration tests.</summary>
public partial class Program;
