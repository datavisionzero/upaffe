using Upaffe.Api.Hosting;

namespace Upaffe.Api.Http;

/// <summary>Attaches the running version to every response.</summary>
public static class VersionHeader
{
    public const string Name = "Upaffe-Version";

    public static IApplicationBuilder UseUpaffeVersion(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[Name] = InstanceVersion.Value;
                return Task.CompletedTask;
            });
            return next(context);
        });
}
