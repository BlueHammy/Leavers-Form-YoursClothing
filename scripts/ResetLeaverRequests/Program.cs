using LeaversForm.Data;
using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using Microsoft.EntityFrameworkCore;

var dbPath = args.FirstOrDefault(a => !a.StartsWith('-'))
    ?? FindDefaultDatabasePath();

if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Database not found: {dbPath}");
    return 1;
}

var options = new DbContextOptionsBuilder<LeaversFormDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;

await using var db = new LeaversFormDbContext(options);

if (args.Contains("--list", StringComparer.OrdinalIgnoreCase))
{
    var rows = await db.LeaverRequests
        .AsNoTracking()
        .OrderBy(x => x.Id)
        .Select(x => new { x.Id, x.LeaverDisplayName, x.LeaverUserPrincipalName, x.Status, x.ScriptLastAttemptAt, x.ScriptAutoRetryCount })
        .ToListAsync();

    Console.WriteLine($"Database: {dbPath}");
    foreach (var row in rows)
        Console.WriteLine($"{row.Id}\t{row.Status}\t{row.LeaverDisplayName}\t{row.LeaverUserPrincipalName}\tattempt={row.ScriptLastAttemptAt:u}\tretries={row.ScriptAutoRetryCount}");
    return 0;
}

var failIdArg = args.FirstOrDefault(a => a.StartsWith("--fail-id=", StringComparison.OrdinalIgnoreCase));
if (failIdArg is not null && int.TryParse(failIdArg["--fail-id=".Length..], out var failId))
{
    var request = await db.LeaverRequests.SingleOrDefaultAsync(x => x.Id == failId);
    if (request is null)
    {
        Console.Error.WriteLine($"No request with id {failId} found.");
        return 1;
    }

    request.Status = LeaverStatus.Failed;
    request.ScriptExecutionStatus = ScriptExecutionStatus.Failed;
    request.ScriptExecutedAt = null;
    request.ScriptLastFailedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    Console.WriteLine($"Marked request {failId} ({request.LeaverDisplayName}) as Failed for retry.");
    return 0;
}

var retryIdArg = args.FirstOrDefault(a => a.StartsWith("--retry-id=", StringComparison.OrdinalIgnoreCase));
if (retryIdArg is not null && int.TryParse(retryIdArg["--retry-id=".Length..], out var retryId))
{
    var request = await db.LeaverRequests.SingleOrDefaultAsync(x => x.Id == retryId);
    if (request is null)
    {
        Console.Error.WriteLine($"No request with id {retryId} found.");
        return 1;
    }

    request.Status = LeaverStatus.Scheduled;
    request.ScriptExecutionStatus = ScriptExecutionStatus.NotRun;
    request.ScriptExecutedAt = null;
    request.ScriptExecutionLogPath = null;
    request.ScriptLastAttemptAt = null;
    request.ScriptLastFailedAt = null;
    request.ScriptAutoRetryCount = 0;
    request.ScriptEligibleAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    Console.WriteLine($"Reset request {retryId} ({request.LeaverDisplayName}) to Scheduled for retry.");
    return 0;
}

var idArg = args.FirstOrDefault(a => a.StartsWith("--id=", StringComparison.OrdinalIgnoreCase));
if (idArg is not null && int.TryParse(idArg["--id=".Length..], out var requestId))
{
    var deleted = await DbInitializer.DeleteLeaverRequestAsync(db, requestId);
    Console.WriteLine(deleted
        ? $"Deleted leaver request {requestId} from {dbPath}"
        : $"No request with id {requestId} found.");
    return deleted ? 0 : 1;
}

var nameArg = args.FirstOrDefault(a => a.StartsWith("--name=", StringComparison.OrdinalIgnoreCase));
if (nameArg is not null)
{
    var search = nameArg["--name=".Length..];
    var deleted = await DbInitializer.DeleteLeaverRequestsBySearchAsync(db, search);
    Console.WriteLine(deleted > 0
        ? $"Deleted {deleted} request(s) matching '{search}' from {dbPath}"
        : $"No requests matching '{search}' found.");
    return deleted > 0 ? 0 : 1;
}

if (args.Contains("--all", StringComparer.OrdinalIgnoreCase))
{
    var before = await db.LeaverRequests.CountAsync();
    var result = await DbInitializer.ResetLeaverRequestsAsync(db);
    var after = await db.LeaverRequests.CountAsync();
    Console.WriteLine($"Database: {dbPath}");
    Console.WriteLine($"LeaverRequests before: {before}, after: {after}");
    Console.WriteLine($"Deleted {result.RequestsDeleted} requests, {result.AuditLogsDeleted} audit logs, {result.ManagerLinksDeleted} manager links.");
    return after == 0 ? 0 : 1;
}

Console.WriteLine("""
Usage:
  dotnet run --project scripts/ResetLeaverRequests -- --list
  dotnet run --project scripts/ResetLeaverRequests -- --retry-id=19
  dotnet run --project scripts/ResetLeaverRequests -- --id=18
  dotnet run --project scripts/ResetLeaverRequests -- --name=Bachu
  dotnet run --project scripts/ResetLeaverRequests -- --all
  dotnet run --project scripts/ResetLeaverRequests -- [path-to-leaversform.db] --id=18
""");
return 1;

static string FindDefaultDatabasePath()
{
    var directory = new DirectoryInfo(Path.GetFullPath(AppContext.BaseDirectory));
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, "LeaversForm.Web", "leaversform.db");
        if (File.Exists(candidate))
            return candidate;

        directory = directory.Parent;
    }

    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "LeaversForm.Web", "leaversform.db"));
}
