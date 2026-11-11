using Microsoft.EntityFrameworkCore;

namespace Klavyra.Api.Infrastructure;

/// <summary>
/// Brings the database up to date. The first versions of Klavyra created it with EnsureCreated, which leaves no
/// migration history; such a database already has the first migration's tables, so it is marked as having it
/// instead of creating them again. Before changing an existing database a copy is kept next to it.
/// </summary>
public static class DatabaseMigrator
{
    public const string BackupSuffix = ".antes-de-migrar";

    public static async Task MigrateAsync(KlavyraDb db, ILogger? logger = null, CancellationToken ct = default)
    {
        var database = db.Database;
        var legacy = await HasTableAsync(db, "Projects", ct) && !await HasTableAsync(db, "__EFMigrationsHistory", ct);
        var pending = (await database.GetPendingMigrationsAsync(ct)).ToList();
        if (!legacy && pending.Count == 0)
        {
            return;
        }

        if (await HasTableAsync(db, "Projects", ct))
        {
            Backup(db, logger);
        }
        if (legacy)
        {
            var baseline = database.GetMigrations().First();
            await database.ExecuteSqlRawAsync(
                """CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" ("MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY, "ProductVersion" TEXT NOT NULL)""", ct);
            await database.ExecuteSqlRawAsync(
                $"""INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('{baseline}', '10.0.0')""", ct);
            logger?.LogInformation("Existing database marked as migration {Baseline}", baseline);
        }
        await database.MigrateAsync(ct);
    }

    private static async Task<bool> HasTableAsync(KlavyraDb db, string name, CancellationToken ct) =>
        await db.Database.SqlQuery<int>($"SELECT count(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = {name}")
            .SingleAsync(ct) > 0;

    private static void Backup(KlavyraDb db, ILogger? logger)
    {
        var file = db.Database.GetDbConnection().DataSource;
        if (!File.Exists(file))
        {
            return;
        }
        try
        {
            File.Copy(file, file + BackupSuffix, overwrite: true);
        }
        catch (IOException e)
        {
            logger?.LogWarning(e, "Could not back up the database before migrating it");
        }
    }
}
