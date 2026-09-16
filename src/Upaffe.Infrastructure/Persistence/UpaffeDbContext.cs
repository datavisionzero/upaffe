using Microsoft.EntityFrameworkCore;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>The single EF Core schema for upaffe.</summary>
/// <remarks>
/// The first migration is intentionally empty of product tables. Monitoring
/// entities arrive with the feature that defines their rules.
/// </remarks>
public sealed class UpaffeDbContext(DbContextOptions<UpaffeDbContext> options) : DbContext(options);
