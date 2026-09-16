using Microsoft.AspNetCore.OpenApi;
using Upaffe.Api.Hosting;

namespace Upaffe.Api.Http;

/// <summary>The deterministic contract captured at docs/api/openapi.json.</summary>
public static class OpenApiDocument
{
    public static IServiceCollection AddUpaffeOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "upaffe";
            document.Info.Version = InstanceVersion.Value;
            document.Info.Description =
                "The HTTP surface of one upaffe instance. Every endpoint is under /api; "
                + "every response carries Upaffe-Version.";
            document.Servers?.Clear();
            return Task.CompletedTask;
        }));
}
