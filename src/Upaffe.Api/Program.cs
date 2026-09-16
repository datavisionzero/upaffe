using System.Text.Json;
using System.Text.Json.Serialization;
using Upaffe.Api.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.MapGroup("/api").MapHealth();

app.Run();

/// <summary>Visible to the in-process host used by integration tests.</summary>
public partial class Program;
