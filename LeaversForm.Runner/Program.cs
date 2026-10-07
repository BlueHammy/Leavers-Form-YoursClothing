using LeaversForm.Data;
using LeaversForm.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var webContentRoot = FindWebContentRoot(AppContext.BaseDirectory);

if (builder.Environment.IsDevelopment())
{
    var developmentLocal = Path.Combine(webContentRoot, "appsettings.Development.local.json");
    if (File.Exists(developmentLocal))
        builder.Configuration.AddJsonFile(developmentLocal, optional: true, reloadOnChange: false);
}
else if (builder.Environment.IsProduction())
{
    var productionLocal = Path.Combine(builder.Environment.ContentRootPath, "appsettings.Production.local.json");
    if (File.Exists(productionLocal))
        builder.Configuration.AddJsonFile("appsettings.Production.local.json", optional: true, reloadOnChange: false);
}

var connection = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=leaversform.db";
connection = ResolveSqliteConnectionString(connection, webContentRoot);

builder.Services.AddDbContext<LeaversFormDbContext>(options =>
{
    if (connection.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) &&
        !connection.Contains("Server=", StringComparison.OrdinalIgnoreCase))
        options.UseSqlite(connection);
    else
        options.UseSqlServer(connection, x => x.MigrationsAssembly("LeaversForm.Data"));
});
builder.Services.Configure<LeaversFormOptions>(builder.Configuration.GetSection("LeaversForm"));
builder.Services.AddOptions<LeaversFormOptions>()
    .Bind(builder.Configuration.GetSection("LeaversForm"))
    .PostConfigure<IHostEnvironment>(LeaversFormPaths.Configure);
builder.Services.Configure<GraphOptions>(builder.Configuration.GetSection("Graph"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GraphOptions>>().Value);
builder.Services.AddHttpClient();
builder.Services.AddScoped<IScriptEligibilityCalculator, ScriptEligibilityCalculator>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IPowerShellScriptExecutor, PowerShellScriptExecutor>();
builder.Services.AddScoped<ILeaverScriptRepairService, LeaverScriptRepairService>();
builder.Services.AddSingleton<ILeaverScriptExecutionTracker, LeaverScriptExecutionTracker>();
builder.Services.AddSingleton<IAsapLeaverScriptDispatcher, NoOpAsapLeaverScriptDispatcher>();
builder.Services.AddScoped<ILeaverScriptProcessor, LeaverScriptProcessor>();
builder.Services.AddScoped<IGraphEmailService, GraphEmailService>();
builder.Services.AddScoped<IErrorNotificationService, ErrorNotificationService>();
using var host = builder.Build();
using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<LeaversFormDbContext>();
await DbInitializer.InitializeAsync(db, host.Services.GetRequiredService<IConfiguration>());
var processor = scope.ServiceProvider.GetRequiredService<ILeaverScriptProcessor>();

if (args.Contains("--reconcile", StringComparer.OrdinalIgnoreCase))
{
    await processor.ReconcileStuckRequestsAsync();
    return 0;
}

var processIdArg = args.FirstOrDefault(a => a.StartsWith("--process-id=", StringComparison.OrdinalIgnoreCase));
if (processIdArg is not null && int.TryParse(processIdArg["--process-id=".Length..], out var processId))
{
    await processor.ProcessRequestAsync(processId, cancellationToken: default);
    return 0;
}

await processor.ProcessDueRequestsAsync();
return 0;

static string ResolveSqliteConnectionString(string connection, string contentRootPath)
{
    const string prefix = "Data Source=";
    if (!connection.Contains(prefix, StringComparison.OrdinalIgnoreCase) ||
        connection.Contains("Server=", StringComparison.OrdinalIgnoreCase))
        return connection;

    var semi = connection.IndexOf(';');
    var dataSource = semi >= 0
        ? connection[prefix.Length..semi]
        : connection[prefix.Length..];

    if (Path.IsPathRooted(dataSource))
        return connection;

    var absolute = Path.Combine(contentRootPath, dataSource);
    return semi >= 0
        ? $"{prefix}{absolute};{connection[(semi + 1)..]}"
        : $"{prefix}{absolute}";
}

static string FindWebContentRoot(string startDirectory)
{
    var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, "LeaversForm.Web");
        if (Directory.Exists(candidate))
            return candidate;

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate LeaversForm.Web project directory.");
}
