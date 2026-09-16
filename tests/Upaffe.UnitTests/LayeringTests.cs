using System.Xml.Linq;

namespace Upaffe.UnitTests;

/// <summary>Executable proof of the dependency directions in docs/codebase.md.</summary>
public sealed class LayeringTests
{
    [Fact]
    public void Domain_depends_on_nothing()
    {
        var domain = Layer.Read("Upaffe.Domain");
        Assert.Empty(domain.ProjectReferences);
        Assert.Empty(domain.PackageReferences);
    }

    [Fact]
    public void Application_depends_on_domain_only() =>
        Assert.Equal(["Upaffe.Domain"], Layer.Read("Upaffe.Application").ProjectReferences);

    [Fact]
    public void Infrastructure_depends_on_application_only() =>
        Assert.Equal(["Upaffe.Application"], Layer.Read("Upaffe.Infrastructure").ProjectReferences);

    [Fact]
    public void Api_is_the_composition_root() =>
        Assert.Equal(
            ["Upaffe.Application", "Upaffe.Infrastructure"],
            Layer.Read("Upaffe.Api").ProjectReferences);

    private sealed record Layer(
        IReadOnlyList<string> ProjectReferences,
        IReadOnlyList<string> PackageReferences)
    {
        public static Layer Read(string project)
        {
            var document = XDocument.Load(
                Path.Combine(RepositoryRoot.Path, "src", project, $"{project}.csproj"));
            return new Layer(
                References(document, "ProjectReference"),
                References(document, "PackageReference"));
        }

        private static IReadOnlyList<string> References(XDocument project, string element) =>
            [.. project.Descendants(element)
                .Select(reference => Path.GetFileNameWithoutExtension(
                    reference.Attribute("Include")?.Value ?? string.Empty))
                .Order(StringComparer.Ordinal)];
    }
}
