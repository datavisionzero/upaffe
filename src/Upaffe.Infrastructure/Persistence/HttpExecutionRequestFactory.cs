using System.Text;
using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;

namespace Upaffe.Infrastructure.Persistence;

internal static class HttpExecutionRequestFactory
{
    public static async Task<HttpExecutionRequest> CreateAsync(
        UpaffeDbContext context,
        HttpMonitor monitor,
        CancellationToken cancellationToken)
    {
        var targetSecret = await context.HttpMonitorSecrets.AsNoTracking().SingleAsync(
            value => value.MonitorId == monitor.Id,
            cancellationToken);
        var headers = await context.HttpMonitorHeaders.AsNoTracking()
            .Where(value => value.MonitorId == monitor.Id)
            .Join(
                context.HttpMonitorHeaderSecrets.AsNoTracking(),
                header => header.Id,
                secret => secret.HeaderId,
                (header, secret) => new { header.Name, secret.ValueUtf8 })
            .OrderBy(value => value.Name)
            .ToListAsync(cancellationToken);
        return new(
            monitor.TargetUrl + targetSecret.RevealTargetQuery(),
            headers.Select(value => new HttpExecutionHeader(
                value.Name,
                new UTF8Encoding(false, true).GetString(value.ValueUtf8))).ToArray(),
            monitor.ExpectedStatusCode,
            monitor.TextCondition,
            monitor.TextFragment,
            monitor.TimeoutSeconds);
    }
}
