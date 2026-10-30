using Klavyra.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Klavyra.Api.Infrastructure;

public sealed class KlavyraDb(DbContextOptions<KlavyraDb> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var project = model.Entity<Project>();
        project.Property(p => p.Title).HasMaxLength(200);
        project.Property(p => p.Engine).HasMaxLength(40);
        project.Property(p => p.Model).HasMaxLength(20);
        project.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        // SQLite cannot ORDER BY DateTimeOffset; store it as UTC ticks.
        project.Property(p => p.CreatedAt).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        project.HasIndex(p => p.CreatedAt);
    }
}
