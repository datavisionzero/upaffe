namespace Upaffe.IntegrationTests;

internal static class RepositoryRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(System.IO.Path.Combine(directory.FullName, "Upaffe.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("No Upaffe.slnx above the test binary.");
    }
}
