using LeaversForm.Data;
using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LeaversForm.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(
        LeaversFormDbContext db,
        IConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await ApplySchemaPatchesAsync(db, cancellationToken);
        await SeedExclusionsAsync(db, cancellationToken);
        await SeedAppUsersAsync(db, cancellationToken);
        if (configuration is not null)
            await AuthorisingManagerSync.SyncFromConfigurationAsync(db, configuration, cancellationToken);
    }

    /// <summary>
    /// Deletes all leaver requests and related audit/manager link rows. Authorising managers, exclusions, and app users are kept.
    /// </summary>
    public static async Task<LeaverRequestResetResult> ResetLeaverRequestsAsync(
        LeaversFormDbContext db,
        CancellationToken cancellationToken = default)
    {
        var auditLogsDeleted = await db.AuditLogs
            .Where(x => x.LeaverRequestId != null)
            .ExecuteDeleteAsync(cancellationToken);
        var managerLinksDeleted = await db.LeaverRequestAuthorisingManagers
            .ExecuteDeleteAsync(cancellationToken);
        var requestsDeleted = await db.LeaverRequests
            .ExecuteDeleteAsync(cancellationToken);

        return new LeaverRequestResetResult(auditLogsDeleted, managerLinksDeleted, requestsDeleted);
    }

    /// <summary>
    /// Deletes one leaver request and its audit/manager links (e.g. false Completed from test mode).
    /// </summary>
    public static async Task<bool> DeleteLeaverRequestAsync(
        LeaversFormDbContext db,
        int requestId,
        CancellationToken cancellationToken = default)
    {
        if (!await db.LeaverRequests.AnyAsync(x => x.Id == requestId, cancellationToken))
            return false;

        await db.AuditLogs
            .Where(x => x.LeaverRequestId == requestId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.LeaverRequestAuthorisingManagers
            .Where(x => x.LeaverRequestId == requestId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.LeaverRequests
            .Where(x => x.Id == requestId)
            .ExecuteDeleteAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Deletes leaver requests whose display name or UPN contains the search text (case-insensitive).
    /// </summary>
    public static async Task<int> DeleteLeaverRequestsBySearchAsync(
        LeaversFormDbContext db,
        string search,
        CancellationToken cancellationToken = default)
    {
        var pattern = search.Trim();
        if (string.IsNullOrWhiteSpace(pattern))
            return 0;

        var ids = await db.LeaverRequests
            .Where(x =>
                EF.Functions.Like(x.LeaverDisplayName, $"%{pattern}%")
                || EF.Functions.Like(x.LeaverUserPrincipalName, $"%{pattern}%"))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var deleted = 0;
        foreach (var id in ids)
        {
            if (await DeleteLeaverRequestAsync(db, id, cancellationToken))
                deleted++;
        }

        return deleted;
    }

    public sealed record LeaverRequestResetResult(int AuditLogsDeleted, int ManagerLinksDeleted, int RequestsDeleted);

    private static async Task ApplySchemaPatchesAsync(LeaversFormDbContext db, CancellationToken cancellationToken)
    {
        if (db.Database.IsSqlite())
        {
            await AddSqliteColumnIfMissingAsync(db, "LeaverRequests", "SpecialRequests", "TEXT NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "LeaverRequests", "DisableAsap", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "LeaverRequests", "ScriptLastAttemptAt", "TEXT NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "LeaverRequests", "ScriptLastFailedAt", "TEXT NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "LeaverRequests", "ScriptAutoRetryCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "LeaverRequests", "ApprovedAt", "TEXT NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "LeaverRequests", "ApprovedByName", "TEXT NULL", cancellationToken);
            await AddSqliteColumnIfMissingAsync(db, "LeaverRequests", "ApprovedByEmail", "TEXT NULL", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE LeaverRequests SET ScriptLastAttemptAt = NULL WHERE ScriptLastAttemptAt = '';
                UPDATE LeaverRequests SET ScriptLastFailedAt = NULL WHERE ScriptLastFailedAt = '';
                """, cancellationToken);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "LeaverExclusions" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_LeaverExclusions" PRIMARY KEY AUTOINCREMENT,
                    "MatchType" TEXT NOT NULL,
                    "MatchValue" TEXT NOT NULL,
                    "Description" TEXT NULL,
                    "IsActive" INTEGER NOT NULL DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS "AppUsers" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_AppUsers" PRIMARY KEY AUTOINCREMENT,
                    "Email" TEXT NOT NULL,
                    "DisplayName" TEXT NOT NULL,
                    "Role" TEXT NOT NULL,
                    "IsActive" INTEGER NOT NULL DEFAULT 1
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppUsers_Email" ON "AppUsers" ("Email");
                """, cancellationToken);
            return;
        }

        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('LeaverRequests', 'SpecialRequests') IS NULL
                ALTER TABLE LeaverRequests ADD SpecialRequests nvarchar(4000) NULL;
            IF COL_LENGTH('LeaverRequests', 'DisableAsap') IS NULL
                ALTER TABLE LeaverRequests ADD DisableAsap bit NOT NULL CONSTRAINT DF_LeaverRequests_DisableAsap DEFAULT 0;
            IF COL_LENGTH('LeaverRequests', 'ScriptLastAttemptAt') IS NULL
                ALTER TABLE LeaverRequests ADD ScriptLastAttemptAt datetime2 NULL;
            IF COL_LENGTH('LeaverRequests', 'ScriptLastFailedAt') IS NULL
                ALTER TABLE LeaverRequests ADD ScriptLastFailedAt datetime2 NULL;
            IF COL_LENGTH('LeaverRequests', 'ScriptAutoRetryCount') IS NULL
                ALTER TABLE LeaverRequests ADD ScriptAutoRetryCount int NOT NULL CONSTRAINT DF_LeaverRequests_ScriptAutoRetryCount DEFAULT 0;
            IF COL_LENGTH('LeaverRequests', 'ApprovedAt') IS NULL
                ALTER TABLE LeaverRequests ADD ApprovedAt datetime2 NULL;
            IF COL_LENGTH('LeaverRequests', 'ApprovedByName') IS NULL
                ALTER TABLE LeaverRequests ADD ApprovedByName nvarchar(200) NULL;
            IF COL_LENGTH('LeaverRequests', 'ApprovedByEmail') IS NULL
                ALTER TABLE LeaverRequests ADD ApprovedByEmail nvarchar(320) NULL;
            IF OBJECT_ID(N'LeaverExclusions', N'U') IS NULL
            BEGIN
                CREATE TABLE LeaverExclusions (
                    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    MatchType nvarchar(30) NOT NULL,
                    MatchValue nvarchar(320) NOT NULL,
                    Description nvarchar(500) NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_LeaverExclusions_IsActive DEFAULT 1
                );
                CREATE INDEX IX_LeaverExclusions_IsActive ON LeaverExclusions (IsActive);
            END
            IF OBJECT_ID(N'AppUsers', N'U') IS NULL
            BEGIN
                CREATE TABLE AppUsers (
                    Id int NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    Email nvarchar(320) NOT NULL,
                    DisplayName nvarchar(200) NOT NULL,
                    Role nvarchar(20) NOT NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_AppUsers_IsActive DEFAULT 1
                );
                CREATE UNIQUE INDEX IX_AppUsers_Email ON AppUsers (Email);
                CREATE INDEX IX_AppUsers_IsActive ON AppUsers (IsActive);
            END
            """, cancellationToken);
    }

    private static async Task SeedExclusionsAsync(LeaversFormDbContext db, CancellationToken cancellationToken)
    {
        if (await db.LeaverExclusions.AnyAsync(cancellationToken))
            return;

        foreach (var exclusion in DefaultExclusions)
            db.LeaverExclusions.Add(exclusion);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static readonly LeaverExclusion[] DefaultExclusions =
    [
        new() { MatchType = LeaverExclusionMatchType.AccountPrefix, MatchValue = "ADM_", Description = "Admin accounts" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Michael Newton" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "David Lane" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Vlad Kucherov" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Leigh Porter" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Andrew Killingsworth" },
        new() { MatchType = LeaverExclusionMatchType.EmailExact, MatchValue = "IT@yoursclothing.co.uk", Description = "Shared IT mailbox" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Antony Talbot" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Beth Watson" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Claire Atkinson" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "David Preece" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Hannah Quy" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Harley Killingsworth" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Kay Clay" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Peter Rodgers" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Monika Tyton" },
        new() { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Patrycja Lyszczarl" },
        new() { MatchType = LeaverExclusionMatchType.TextContains, MatchValue = "CST", Description = "CST accounts" }
    ];

    private static async Task SeedAppUsersAsync(LeaversFormDbContext db, CancellationToken cancellationToken)
    {
        if (await db.AppUsers.AnyAsync(cancellationToken))
            return;

        foreach (var user in DefaultAppUsers)
            db.AppUsers.Add(user);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static readonly AppUser[] DefaultAppUsers =
    [
        new() { Email = "michaelnewton@yoursclothing.co.uk", DisplayName = "Michael Newton", Role = AppRole.SuperUser },
        new() { Email = "davidlane@yoursclothing.co.uk", DisplayName = "David Lane", Role = AppRole.SuperUser },
        new() { Email = "vladkucherov@yoursclothing.co.uk", DisplayName = "Vlad Kucherov", Role = AppRole.SuperUser }
    ];

    private static async Task AddSqliteColumnIfMissingAsync(
        LeaversFormDbContext db,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var exists = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        if (!exists)
        {
            if (!IsSafeSqliteIdentifier(table) || !IsSafeSqliteIdentifier(column))
                throw new InvalidOperationException("Invalid SQLite identifier for schema patch.");

            if (!AllowedSqliteColumnDefinitions.Contains(definition))
                throw new InvalidOperationException($"Unsupported column definition: {definition}");

            // Identifiers and definitions are validated above; callers pass hardcoded schema patch values only.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE {table} ADD COLUMN {column} {definition}",
                cancellationToken);
#pragma warning restore EF1002
        }
    }

    private static bool IsSafeSqliteIdentifier(string value) =>
        !string.IsNullOrEmpty(value) && value.All(static c => char.IsLetterOrDigit(c) || c == '_');

    private static readonly HashSet<string> AllowedSqliteColumnDefinitions = new(StringComparer.OrdinalIgnoreCase)
    {
        "TEXT NULL",
        "INTEGER NOT NULL DEFAULT 0"
    };
}
