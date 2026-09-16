using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>Builds the model for dotnet-ef without starting the API.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<UpaffeDbContext>
{
    public UpaffeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<UpaffeDbContext>()
            .UseNpgsql("Host=design-time;Database=upaffe;Username=upaffe")
            .Options;
        return new UpaffeDbContext(options);
    }
}
