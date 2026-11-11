using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Klavyra.Api.Infrastructure;

/// <summary>Only for `dotnet ef migrations add`: it needs a context without starting the whole application.</summary>
public sealed class KlavyraDbFactory : IDesignTimeDbContextFactory<KlavyraDb>
{
    public KlavyraDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<KlavyraDb>().UseSqlite("Data Source=design-time.db").Options);
}
