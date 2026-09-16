namespace Upaffe.Domain.Access;

/// <summary>The bounded secret the operator uses to sign in.</summary>
public static class Password
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 200;

    public static string Validate(string? password)
    {
        if (password is null || password.Length is < MinimumLength or > MaximumLength)
        {
            throw new ArgumentException(
                $"A password must be {MinimumLength}-{MaximumLength} characters.",
                nameof(password));
        }

        return password;
    }
}
