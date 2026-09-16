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

    public static Refusal SignInRejected() =>
        new("sign_in_rejected", "The email address or password is not correct.");

    public static Refusal AuthenticationRequired() =>
        new("authentication_required", "Sign in with a browser session.");

    public static Refusal AuthenticationRejected() =>
        new("authentication_rejected", "The presented authentication was rejected.");

    public static Refusal Forbidden() =>
        new("forbidden", "This access path cannot perform that operation.");

    public static Refusal Conflict(string message) => new("conflict", message);

    public static Refusal NotFound(string message) => new("not_found", message);
}
