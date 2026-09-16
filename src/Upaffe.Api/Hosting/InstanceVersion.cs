using System.Reflection;

namespace Upaffe.Api.Hosting;

/// <summary>The release tag, or 0.0.0-dev for an untagged build.</summary>
public static class InstanceVersion
{
    public static readonly string Value = Read();

    private static string Read()
    {
        var informational = typeof(InstanceVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0-dev";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
