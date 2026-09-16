using Upaffe.Domain.Monitoring;

namespace Upaffe.Application.Ports;

public sealed record HttpExecutionHeader(string Name, string Value);

public sealed record HttpExecutionRequest(
    string TargetUrl,
    IReadOnlyList<HttpExecutionHeader> Headers,
    int ExpectedStatusCode,
    TextCondition TextCondition,
    string? TextFragment,
    int TimeoutSeconds);

public sealed record HttpExecutionResult(
    bool Succeeded,
    string? ReasonCode,
    string Message,
    int? StatusCode,
    int ResponseTimeMilliseconds,
    string? EffectiveUrl);

/// <summary>Executes one bounded observation without scheduling or incident transitions.</summary>
public interface IHttpCheckExecutor
{
    Task<HttpExecutionResult> ExecuteAsync(
        HttpExecutionRequest request,
        CancellationToken cancellationToken);
}
