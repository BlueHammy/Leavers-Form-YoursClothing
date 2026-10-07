using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaversForm.Services;

public sealed class LeaversFormProcessingModeService(
    IOptionsMonitor<LeaversFormOptions> optionsMonitor,
    IConfiguration configuration,
    IHostEnvironment environment,
    IServiceScopeFactory scopeFactory,
    ILogger<LeaversFormProcessingModeService> logger) : ILeaversFormProcessingModeService
{
    private readonly object _gate = new();
    private bool _isScriptTestMode = configuration.GetValue("LeaversForm:ScriptTestMode", true);

    public bool IsScriptTestModeEffective
    {
        get
        {
            lock (_gate)
                return _isScriptTestMode;
        }
    }

    public string? LocalSettingsPath => ResolveLocalSettingsPath(environment);

    public async Task SetScriptTestModeAsync(bool testMode, ClaimsPrincipal performer, CancellationToken cancellationToken = default)
    {
        string oldMode;
        lock (_gate)
        {
            oldMode = _isScriptTestMode ? "TEST" : "LIVE";
            _isScriptTestMode = testMode;
        }

        try
        {
            PersistToLocalSettings(testMode);
        }
        catch (Exception ex)
        {
            lock (_gate)
                _isScriptTestMode = oldMode == "TEST";

            logger.LogError(ex, "Failed to persist ScriptTestMode={TestMode} to local settings", testMode);
            throw new InvalidOperationException("Could not save processing mode to settings file.", ex);
        }

        var newMode = testMode ? "TEST" : "LIVE";
        logger.LogWarning(
            "Processing mode changed from {OldMode} to {NewMode} by {User}",
            oldMode,
            newMode,
            performer.Identity?.Name ?? "unknown");

        await AuditModeChangeAsync(performer, oldMode, newMode, testMode, cancellationToken);

        _ = optionsMonitor.CurrentValue;
    }

    private async Task AuditModeChangeAsync(
        ClaimsPrincipal performer,
        string oldMode,
        string newMode,
        bool testMode,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await audit.AddAsync(
            "ProcessingModeChanged",
            performer,
            leaverRequestId: null,
            oldValue: oldMode,
            newValue: newMode,
            notes: testMode
                ? "Account disables are stopped for new processing attempts."
                : "Live processing enabled — leaver scripts will run.",
            cancellationToken: cancellationToken);
    }

    internal static string? ResolveLocalSettingsPath(IHostEnvironment environment) =>
        LocalAppSettingsPaths.Resolve(environment);

    private void PersistToLocalSettings(bool testMode)
    {
        var path = LocalSettingsPath
            ?? throw new InvalidOperationException("No local settings file is configured for this environment.");

        JsonObject root;
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            root = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }

        if (root["LeaversForm"] is not JsonObject leaversSection)
        {
            leaversSection = new JsonObject();
            root["LeaversForm"] = leaversSection;
        }

        leaversSection["ScriptTestMode"] = testMode;

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        logger.LogInformation("Persisted ScriptTestMode={TestMode} to {Path}", testMode, path);
    }
}
