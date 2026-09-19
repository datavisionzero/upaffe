using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Application.Projects;

public sealed class ReadProjectReport(IProjectReportStore reports, TimeProvider clock)
{
    public async Task<ProjectReport> ExecuteAsync(Identity identity, string? key,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var report = await reports.ReadAsync(ProjectValidation.ValidKey(key),
            clock.GetUtcNow(), cancellationToken);
        return report ?? throw Refusal.NotFound("No such live project.");
    }
}
