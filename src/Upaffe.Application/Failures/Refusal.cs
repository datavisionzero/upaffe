namespace Upaffe.Application.Failures;

/// <summary>A stable application refusal suitable for every transport.</summary>
public sealed class Refusal(
    string code,
    string message,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;

    public static Refusal Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new("validation", "One or more values are not acceptable.", errors);

    public static Refusal BootstrapRejected() =>
        new("bootstrap_rejected", "The bootstrap proof was rejected.");

    public static Refusal BootstrapClosed() =>
        new("bootstrap_closed", "This instance already has its operator.");
}
