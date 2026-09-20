namespace Upaffe.Domain.Monitoring;

/// <summary>Operator-written description of what a monitor covers.</summary>
public static class MonitorPurpose
{
    public const int MaximumLength = 240;

    public static string? Normalize(string? value)
    {
        var purpose = value?.Trim();
        if (string.IsNullOrEmpty(purpose)) return null;
        if (purpose.EnumerateRunes().Count() > MaximumLength)
            throw new ArgumentException("A monitor purpose may contain at most 240 characters.", nameof(value));
        return purpose;
    }
}
