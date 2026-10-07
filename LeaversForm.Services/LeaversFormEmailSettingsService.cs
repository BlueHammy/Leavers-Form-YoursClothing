using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaversForm.Services;

public sealed record LeaversEmailSettings(
    string ItCcAddress,
    string HrCcAddress,
    string ErrorAlertMailbox,
    string RedirectAllEmailsTo)
{
    public bool RedirectActive => !string.IsNullOrWhiteSpace(RedirectAllEmailsTo);
}

public sealed class LeaversFormEmailSettingsService(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILeaversFormProcessingModeService processingMode,
    IOptions<GraphOptions> graphOptions,
    IServiceScopeFactory scopeFactory,
    ILogger<LeaversFormEmailSettingsService> logger) : ILeaversFormEmailSettingsService
{
    private readonly object _gate = new();
    private LeaversEmailSettings _effective = ReadFromConfiguration(configuration);

    public LeaversEmailSettings Current
    {
        get
        {
            lock (_gate)
                return _effective;
        }
    }

    public string ItCcAddress => Current.ItCcAddress;
    public string HrCcAddress => Current.HrCcAddress;
    public string ErrorAlertMailbox => Current.ErrorAlertMailbox;
    public string RedirectAllEmailsTo => Current.RedirectAllEmailsTo;
    public bool RedirectActive => Current.RedirectActive;

    public bool CcHrOnSubmissionEmails =>
        !RedirectActive
        && !graphOptions.Value.EmailTestMode
        && !string.IsNullOrWhiteSpace(HrCcAddress);

    public async Task UpdateAsync(LeaversEmailSettings settings, ClaimsPrincipal performer, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        Validate(normalized);

        LeaversEmailSettings previous;
        lock (_gate)
        {
            previous = _effective;
            _effective = normalized;
        }

        try
        {
            Persist(normalized);
        }
        catch (Exception ex)
        {
            lock (_gate)
                _effective = previous;

            logger.LogError(ex, "Failed to persist email settings");
            throw new InvalidOperationException("Could not save email settings to settings file.", ex);
        }

        logger.LogInformation(
            "Email settings updated by {User}: IT CC={ItCc}, HR CC={HrCc}, errors={Errors}, redirect={Redirect}",
            performer.Identity?.Name ?? "unknown",
            normalized.ItCcAddress,
            normalized.HrCcAddress,
            normalized.ErrorAlertMailbox,
            string.IsNullOrWhiteSpace(normalized.RedirectAllEmailsTo) ? "(none)" : normalized.RedirectAllEmailsTo);

        await AuditChangeAsync(performer, previous, normalized, cancellationToken);
    }

    private async Task AuditChangeAsync(
        ClaimsPrincipal performer,
        LeaversEmailSettings previous,
        LeaversEmailSettings updated,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await audit.AddAsync(
            "EmailSettingsChanged",
            performer,
            leaverRequestId: null,
            oldValue: FormatForAudit(previous),
            newValue: FormatForAudit(updated),
            notes: updated.RedirectActive
                ? "All outbound mail is redirected to the testing address."
                : "Mail goes to real recipients; IT CC and error alerts use configured addresses.",
            cancellationToken: cancellationToken);
    }

    private static string FormatForAudit(LeaversEmailSettings settings) =>
        $"IT CC={settings.ItCcAddress}; HR CC={settings.HrCcAddress}; errors={settings.ErrorAlertMailbox}; redirect={(settings.RedirectActive ? settings.RedirectAllEmailsTo : "(none)")}";

    private void Persist(LeaversEmailSettings settings)
    {
        var path = processingMode.LocalSettingsPath
            ?? LocalAppSettingsPaths.Resolve(environment)
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

        if (root["Graph"] is not JsonObject graphSection)
        {
            graphSection = new JsonObject();
            root["Graph"] = graphSection;
        }

        graphSection["ItCcAddress"] = settings.ItCcAddress;
        graphSection["HrCcAddress"] = settings.HrCcAddress;
        graphSection["ErrorAlertMailbox"] = settings.ErrorAlertMailbox;
        graphSection["RedirectAllEmailsTo"] = settings.RedirectAllEmailsTo;

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        logger.LogInformation("Persisted email settings to {Path}", path);
    }

    private static LeaversEmailSettings ReadFromConfiguration(IConfiguration configuration) =>
        Normalize(new LeaversEmailSettings(
            configuration["Graph:ItCcAddress"] ?? string.Empty,
            configuration["Graph:HrCcAddress"] ?? string.Empty,
            configuration["Graph:ErrorAlertMailbox"] ?? string.Empty,
            configuration["Graph:RedirectAllEmailsTo"] ?? string.Empty));

    private static LeaversEmailSettings Normalize(LeaversEmailSettings settings) =>
        new(
            settings.ItCcAddress.Trim(),
            settings.HrCcAddress.Trim(),
            settings.ErrorAlertMailbox.Trim(),
            settings.RedirectAllEmailsTo.Trim());

    private static void Validate(LeaversEmailSettings settings)
    {
        ValidateEmail(settings.ItCcAddress, nameof(settings.ItCcAddress));
        ValidateEmail(settings.HrCcAddress, nameof(settings.HrCcAddress));
        ValidateEmail(settings.ErrorAlertMailbox, nameof(settings.ErrorAlertMailbox));
        if (!string.IsNullOrWhiteSpace(settings.RedirectAllEmailsTo))
            ValidateEmail(settings.RedirectAllEmailsTo, nameof(settings.RedirectAllEmailsTo));
    }

    private static void ValidateEmail(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{fieldName} is required.", fieldName);

        if (value.Contains(' ', StringComparison.Ordinal) ||
            !value.Contains('@', StringComparison.Ordinal) ||
            value.StartsWith('@') ||
            value.EndsWith('@'))
            throw new ArgumentException($"{fieldName} must be a valid email address.", fieldName);
    }
}
