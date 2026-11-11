using Klavyra.Api.Domain;
using Klavyra.Api.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Klavyra.Api.Tests;

public sealed class DatabaseMigratorTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("klavyra-db").FullName;
    private string File_ => Path.Combine(_folder, "klavyra.db");

    private KlavyraDb Open() =>
        new(new DbContextOptionsBuilder<KlavyraDb>().UseSqlite($"Data Source={File_};Pooling=False").Options);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static Project Song(string title) => new() { Title = title, Engine = "bytedance_piano", Model = "small" };

    [Fact]
    public async Task A_new_database_is_created_by_the_migration()
    {
        await using (var db = Open())
        {
            await DatabaseMigrator.MigrateAsync(db, ct: TestContext.Current.CancellationToken);
            db.Projects.Add(Song("Nueva"));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using var check = Open();
        Assert.Single(await check.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Nueva", (await check.Projects.SingleAsync(TestContext.Current.CancellationToken)).Title);
        Assert.False(File.Exists(File_ + DatabaseMigrator.BackupSuffix)); // nothing existed to protect
    }

    [Fact]
    public async Task A_database_made_by_the_first_versions_keeps_its_projects()
    {
        Guid id;
        await using (var old = Open())
        {
            await old.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken); // how Klavyra 0.1 made it
            var song = Song("Für Elise");
            song.Files = ["notes", "midi"];
            id = song.Id;
            old.Projects.Add(song);
            await old.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = Open())
        {
            await DatabaseMigrator.MigrateAsync(db, ct: TestContext.Current.CancellationToken);
        }

        await using var check = Open();
        var kept = await check.Projects.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(id, kept.Id);
        Assert.Equal(["notes", "midi"], kept.Files);
        Assert.Single(await check.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await check.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.True(File.Exists(File_ + DatabaseMigrator.BackupSuffix));
    }

    [Fact]
    public async Task Migrating_again_changes_nothing()
    {
        await using var db = Open();
        await DatabaseMigrator.MigrateAsync(db, ct: TestContext.Current.CancellationToken);
        db.Projects.Add(Song("Una"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await DatabaseMigrator.MigrateAsync(db, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1, await db.Projects.CountAsync(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(File_ + DatabaseMigrator.BackupSuffix));
    }
}
