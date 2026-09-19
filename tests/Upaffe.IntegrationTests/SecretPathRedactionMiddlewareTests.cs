using Microsoft.AspNetCore.Http;
using Upaffe.Api.Http;

namespace Upaffe.IntegrationTests;

public sealed class SecretPathRedactionMiddlewareTests
{
    [Theory]
    [InlineData("/api/report/generated-secret", true)]
    [InlineData("/api/report/generated-secret/extra", false)]
    [InlineData("/api/report/generated-secret/", false)]
    public async Task Every_secret_path_is_removed_before_the_next_component(
        string path, bool valid)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var observed = string.Empty;
        var middleware = new SecretPathRedactionMiddleware(next =>
        {
            observed = next.Request.Path.Value ?? string.Empty;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.DoesNotContain("generated-secret", observed, StringComparison.Ordinal);
        Assert.Equal(valid ? "generated-secret" : null,
            context.Items[SecretPathRedactionMiddleware.SecretItem] as string);
    }
}
