using LeaversForm.Data;
using LeaversForm.Services;
using LeaversForm.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.EnvironmentName.Equals("SemiLive", StringComparison.OrdinalIgnoreCase))
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
    var semiLiveLocal = Path.Combine(builder.Environment.ContentRootPath, "appsettings.SemiLive.local.json");
    if (File.Exists(semiLiveLocal))
        builder.Configuration.AddJsonFile("appsettings.SemiLive.local.json", optional: true, reloadOnChange: true);
}
else if (builder.Environment.IsDevelopment())
{
    var developmentLocal = Path.Combine(builder.Environment.ContentRootPath, "appsettings.Development.local.json");
    if (File.Exists(developmentLocal))
        builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);
}
else if (builder.Environment.IsProduction())
{
    var productionLocal = Path.Combine(builder.Environment.ContentRootPath, "appsettings.Production.local.json");
    if (File.Exists(productionLocal))
        builder.Configuration.AddJsonFile("appsettings.Production.local.json", optional: true, reloadOnChange: true);
}

var lanTestLocal = Path.Combine(builder.Environment.ContentRootPath, "appsettings.LanTest.local.json");
if (File.Exists(lanTestLocal))
    builder.Configuration.AddJsonFile("appsettings.LanTest.local.json", optional: true, reloadOnChange: true);

builder.Host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());
var connection = ResolveSqliteConnectionString(
    builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=leaversform.db",
    builder.Environment.ContentRootPath);
builder.Services.AddDbContext<LeaversFormDbContext>(options =>
{
    if (connection.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) &&
        !connection.Contains("Server=", StringComparison.OrdinalIgnoreCase))
        options.UseSqlite(connection);
    else
        options.UseSqlServer(connection, x => x.MigrationsAssembly("LeaversForm.Data"));
}, ServiceLifetime.Scoped);
builder.Services.Configure<GraphOptions>(builder.Configuration.GetSection("Graph"));
builder.Services.AddOptions<LeaversFormOptions>()
    .Bind(builder.Configuration.GetSection("LeaversForm"))
    .PostConfigure<IHostEnvironment>(LeaversFormPaths.Configure);
builder.Services.AddSingleton<ILeaversFormProcessingModeService, LeaversFormProcessingModeService>();
builder.Services.AddSingleton<ILeaversFormEmailSettingsService, LeaversFormEmailSettingsService>();
builder.Services.AddScoped<ILeaverRequestLogService, LeaverRequestLogService>();
builder.Services.AddScoped<ILeaversOpsService, LeaversOpsService>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GraphOptions>>().Value);
builder.Services.AddHttpClient();
builder.Services.AddScoped<IScriptEligibilityCalculator, ScriptEligibilityCalculator>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ILeaverRequestService, LeaverRequestService>();
builder.Services.AddScoped<ILeaverRequestStatusPresenter, LeaverRequestStatusPresenter>();
builder.Services.AddScoped<IExcelExportService, ExcelExportService>();
builder.Services.AddScoped<IErrorNotificationService, ErrorNotificationService>();
builder.Services.AddScoped<IAuthorisingManagerNotificationService, AuthorisingManagerNotificationService>();
builder.Services.AddScoped<IAdministrativeNotificationService, AdministrativeNotificationService>();
builder.Services.AddScoped<IPowerShellScriptExecutor, PowerShellScriptExecutor>();
builder.Services.AddScoped<ILeaverScriptRepairService, LeaverScriptRepairService>();
builder.Services.AddScoped<ILeaverScriptProcessor, LeaverScriptProcessor>();
builder.Services.AddSingleton<ILeaverScriptExecutionTracker, LeaverScriptExecutionTracker>();
builder.Services.AddSingleton<IAsapLeaverScriptDispatcher, AsapLeaverScriptDispatcher>();
builder.Services.AddHostedService<LeaverReconciliationHostedService>();
builder.Services.AddHostedService<LeaverProcessingHostedService>();
builder.Services.AddScoped<ILeaverExclusionService, LeaverExclusionService>();
builder.Services.AddScoped<IAppUserService, AppUserService>();
builder.Services.AddScoped<IClaimsTransformation, AppRoleClaimsTransformation>();
builder.Services.AddScoped<IGraphUserSearchService, GraphUserSearchService>();
builder.Services.AddScoped<IGraphEmailService, GraphEmailService>();

if (AuthMode.IsSemiLive(builder.Environment) && !AuthMode.UseEntraSso(builder.Configuration))
{
    throw new InvalidOperationException(
        "SemiLive mode requires Azure AD configuration. Set AzureAd:TenantId, AzureAd:ClientId, and AzureAd:ClientSecret " +
        "via user secrets or appsettings.SemiLive.local.json, then restart. See docs/SEMI-LIVE-TESTING.md.");
}

if (builder.Configuration.GetValue("LeaversForm:RequireEntraSso", false) && !AuthMode.UseEntraSso(builder.Configuration))
{
    throw new InvalidOperationException(
        "LAN testing requires Azure AD configuration. Set AzureAd:TenantId, AzureAd:ClientId, and AzureAd:ClientSecret " +
        "via user secrets or appsettings.SemiLive.local.json, then run .\\Start-LanTest.ps1 again. See docs/SEMI-LIVE-TESTING.md.");
}

var useDevelopmentAuth = AuthMode.UseDevelopmentBypass(builder.Environment, builder.Configuration);
if (useDevelopmentAuth)
{
    builder.Services.AddAuthentication("Development")
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>("Development", _ => { });
}
else if (AuthMode.UseEntraSso(builder.Configuration))
{
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(options =>
        {
            builder.Configuration.Bind("AzureAd", options);
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.SaveTokens = true;
            options.CallbackPath = "/signin-oidc";
            options.SignedOutCallbackPath = "/signout-callback-oidc";
            options.SignedOutRedirectUri = "/Account/SignedOut";
            options.Events ??= new OpenIdConnectEvents();
            options.Events.OnSignedOutCallbackRedirect = context =>
            {
                context.Response.Redirect("/Account/SignedOut");
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });
}
else
{
    builder.Services.AddAuthentication(options => { options.DefaultScheme = "Cookies"; }).AddCookie("Cookies");
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("LeaversUser", p => p.RequireAuthenticatedUser());
    options.AddPolicy("LeaversAdmin", p => p.RequireRole(LeaversRoles.Admin, LeaversRoles.SuperUser));
    options.AddPolicy("LeaversSuperUser", p => p.RequireRole(LeaversRoles.SuperUser));
});

var razorPagesBuilder = builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Leavers", "LeaversUser");
    options.Conventions.AuthorizePage("/Leavers/Exclusions", "LeaversSuperUser");
    options.Conventions.AuthorizePage("/Leavers/Roles", "LeaversSuperUser");
    options.Conventions.AuthorizePage("/Leavers/Ops", "LeaversSuperUser");
    options.Conventions.AuthorizePage("/Leavers/Export", "LeaversAdmin");
    options.Conventions.AuthorizePage("/Leavers/Edit", "LeaversAdmin");
    options.Conventions.AuthorizePage("/Leavers/Cancel", "LeaversAdmin");
    options.Conventions.AuthorizePage("/Leavers/Retry", "LeaversAdmin");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
});

if (!useDevelopmentAuth && AuthMode.UseEntraSso(builder.Configuration))
    razorPagesBuilder.AddMicrosoftIdentityUI();

var app = builder.Build();

var processingMode = app.Services.GetRequiredService<ILeaversFormProcessingModeService>();
var scriptTestMode = processingMode.IsScriptTestModeEffective;
var scriptPath = app.Configuration.GetValue<string>("LeaversForm:PowerShellScriptPath") ?? "(not set)";
if (scriptTestMode)
    app.Logger.LogWarning("ScriptTestMode is ENABLED — leaver accounts will NOT be disabled. Use the Ops page or .\\Set-LiveProcessing.ps1 to go live.");
else
    app.Logger.LogWarning("ScriptTestMode is DISABLED — leaver processing is LIVE. Script: {ScriptPath}", scriptPath);

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var errors = context.RequestServices.GetRequiredService<IErrorNotificationService>();
        await errors.NotifyAsync(
            $"Unhandled web request: {context.Request.Method} {context.Request.Path}",
            ex,
            cancellationToken: context.RequestAborted);
        throw;
    }
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || AuthMode.IsSemiLive(app.Environment))
    app.UseDeveloperExceptionPage();
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    await DbInitializer.InitializeAsync(
        scope.ServiceProvider.GetRequiredService<LeaversFormDbContext>(),
        app.Configuration);

    var processor = scope.ServiceProvider.GetRequiredService<ILeaverScriptProcessor>();
    await processor.ReconcileStuckRequestsAsync();
}

var graphSettings = app.Services.GetRequiredService<ILeaversFormEmailSettingsService>();
var emailRedirect = graphSettings.RedirectAllEmailsTo;
if (!string.IsNullOrWhiteSpace(emailRedirect))
    Log.Information("Email redirect enabled — all outbound mail goes to {RedirectAddress}", emailRedirect);
else if (app.Services.GetRequiredService<GraphOptions>().EmailTestMode)
    Log.Information("Email test mode enabled — messages are logged, not sent");
else
    Log.Information("Live email mode enabled — sender mailbox {SenderMailbox}", app.Services.GetRequiredService<GraphOptions>().SenderMailbox);

var configuredBaseUrl = app.Configuration["LeaversForm:BaseUrl"];
if (!string.IsNullOrWhiteSpace(configuredBaseUrl) &&
    !configuredBaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
    Log.Information("LAN testers can open {BaseUrl}", configuredBaseUrl.TrimEnd('/'));

app.MapGet("/api/users/search", async (string? q, IGraphUserSearchService users, ILeaverExclusionService exclusions, CancellationToken token) =>
{
    var results = await users.SearchAsync(q ?? string.Empty, token);
    var allowed = new List<GraphUser>();
    foreach (var user in results)
    {
        if (!user.AccountEnabled)
            continue;

        var evaluation = await exclusions.EvaluateAsync(new LeaverExclusionCheck(
            user.DisplayName,
            user.Mail ?? user.UserPrincipalName,
            user.UserPrincipalName,
            user.OnPremisesSamAccountName), token);
        if (!evaluation.IsExcluded)
            allowed.Add(user);
    }

    return Results.Ok(allowed);
}).RequireAuthorization("LeaversUser");
app.MapGet("/api/leavers/requests/status", async (HttpContext context, string? search, ILeaverRequestService requests, CancellationToken token) =>
    Results.Ok(await requests.ListStatusAsync(search, context.User, token)))
    .RequireAuthorization("LeaversUser");
app.MapGet("/api/leavers/requests/{id:int}/status", async (HttpContext context, int id, ILeaverRequestService requests, CancellationToken token) =>
{
    var status = await requests.GetStatusAsync(id, context.User, token);
    return status is null ? Results.NotFound() : Results.Ok(status);
}).RequireAuthorization("LeaversUser");
app.MapRazorPages();
app.MapControllers();

app.Run();

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
