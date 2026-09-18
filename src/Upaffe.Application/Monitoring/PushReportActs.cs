using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;

namespace Upaffe.Application.Monitoring;

public sealed class SubmitPushReport(IPushReportStore reports, TimeProvider clock)
{
    public async Task<PushReportReceipt> ExecuteAsync(
        string? token,
        Guid? reportId,
        DateTimeOffset? observedAt,
        string? outcome,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw Refusal.ReportingRejected();
        }

        var now = Microseconds(clock.GetUtcNow());
        if (reportId is null || reportId == Guid.Empty)
        {
            throw Refusal.Validation(new Dictionary<string, string[]> { ["report_id"] = ["A non-empty UUID is required."] });
        }

        if (observedAt is null)
        {
            throw Refusal.Validation(new Dictionary<string, string[]> { ["observed_at"] = ["An RFC 3339 observation time is required."] });
        }

        var acceptedOutcome = outcome switch
        {
            "success" => ReportOutcome.Success,
            "failure" => ReportOutcome.Failure,
            _ => throw Refusal.Validation(new Dictionary<string, string[]> { ["outcome"] = ["Outcome must be success or failure."] }),
        };

        PushReport validated;
        var acceptedObservedAt = Microseconds(observedAt.Value);
        try
        {
            validated = PushReport.Receive(Guid.NewGuid(), reportId.Value, 1, 1, acceptedObservedAt, now, acceptedOutcome, reason);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Refusal.Unprocessable(new Dictionary<string, string[]>
            {
                ["observed_at"] = ["Observation time must be no more than 5 minutes ahead or 90 days behind receipt."],
            });
        }
        catch (ArgumentException exception)
        {
            throw Refusal.Validation(new Dictionary<string, string[]> { ["reason"] = [exception.Message] });
        }

        var result = await reports.SubmitAsync(
            token,
            new(validated.ReportId, validated.ObservedAt, validated.Outcome, validated.DiagnosticReason),
            now,
            cancellationToken);
        return result.Outcome switch
        {
            PushReportMutation.Accepted when result.Receipt is not null => result.Receipt,
            PushReportMutation.Rejected => throw Refusal.ReportingRejected(),
            PushReportMutation.Conflict => throw Refusal.ReportIdConflict(),
            PushReportMutation.Paused => throw Refusal.Conflict("Reporting is suspended for this monitor."),
            _ => throw new InvalidOperationException("The report store returned an invalid submission result."),
        };
    }

    private static DateTimeOffset Microseconds(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}
