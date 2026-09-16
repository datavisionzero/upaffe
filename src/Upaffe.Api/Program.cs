using System.Text.Json;
using System.Text.Json.Serialization;
using Upaffe.Api.Hosting;
using Upaffe.Api.Http;
using Upaffe.Application.Ports;
using Upaffe.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var database = DatabaseSettings.FromConnectionString(
    builder.Configuration.GetConnectionString(DatabaseSettings.ConnectionStringName));
builder.Services.AddUpaffeInfrastructure(database);
builder.Services.AddHostedService<SchemaMigrationService>();
builder.Services.AddUpaffeOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.UseUpaffeVersion();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi("/api/openapi/{documentName}.json");

var api = app.MapGroup("/api");
api.MapInstance();
api.MapHealth();

app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Visible to the in-process host used by integration tests.</summary>
public partial class Program;
