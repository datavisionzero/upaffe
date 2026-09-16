namespace Upaffe.Application.Access;

public sealed record BootstrapSettings(string? Secret)
{
    public const string Variable = "UPAFFE_BOOTSTRAP_SECRET";
    public const int MinimumLength = 32;
    public const int MaximumLength = 1024;

    public string? ValidatedSecret()
    {
        if (string.IsNullOrEmpty(Secret))
        {
            return null;
        }

        if (Secret.Length is < MinimumLength or > MaximumLength)
        {
            throw new InvalidOperationException(
                $"{Variable} must be {MinimumLength}-{MaximumLength} characters when it is set.");
        }

        return Secret;
    }
}
