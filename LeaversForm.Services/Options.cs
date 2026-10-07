namespace LeaversForm.Services;

public sealed class GraphOptions
{
    public string SenderMailbox { get; set; } = "IT@yoursclothing.co.uk";
    public string ItCcAddress { get; set; } = string.Empty;
    public string HrCcAddress { get; set; } = string.Empty;
    public string ErrorAlertMailbox { get; set; } = "it@yoursclothing.co.uk";
    public string RedirectAllEmailsTo { get; set; } = string.Empty;
    public bool EmailTestMode { get; set; } = true;
    public string[] AllowedEmailDomains { get; set; } = ["yoursclothing.co.uk", "akretailgroup.co.uk"];
    public bool RequireOnPremisesSyncEnabled { get; set; } = true;
}

public sealed class LeaversFormOptions
{
    public string BaseUrl { get; set; } = "https://localhost";
    public bool EmailTestMode { get; set; } = true;
    public bool ScriptTestMode { get; set; } = true;
    public string PowerShellScriptPath { get; set; } = string.Empty;
    public string PowerShellWorkingDirectory { get; set; } = string.Empty;
    public string PowerShellExecutable { get; set; } = "pwsh.exe";
    public string PowerShellTenantId { get; set; } = string.Empty;
    public string PowerShellAppId { get; set; } = string.Empty;
    public string PowerShellCertificateThumbprint { get; set; } = string.Empty;
    /// <summary>Linux/production: path to automation PFX (private key). Password via env LeaversForm__PowerShellCertificatePassword.</summary>
    public string PowerShellCertificatePath { get; set; } = string.Empty;
    public string PowerShellExchangeOrganization { get; set; } = string.Empty;
    public string PowerShellDomainController { get; set; } = string.Empty;
    public string LogFolderPath { get; set; } = "logs";
    public int ScriptTimeoutMinutes { get; set; } = 15;
    public int ScriptStuckPendingMinutes { get; set; } = 3;
    public int ScriptMaxAutoRetries { get; set; } = 3;
    /// <summary>
    /// When true, LeaverProcess.ps1 skips AD password reset/disable (Entra steps still run). For dev/sandbox testing only.
    /// </summary>
    public bool ScriptSandboxSkipAd { get; set; }
    public int MinLeavingDateDaysInPastForUsers { get; set; }
    public int MinLeavingDateDaysInPastForAdmins { get; set; } = 365;
    public int MaxSubmissionsPerDayForUsers { get; set; } = 3;
}

public sealed record GraphUser(
    string Id,
    string DisplayName,
    string UserPrincipalName,
    string? Mail,
    string? Department,
    bool AccountEnabled = true,
    string? OnPremisesSamAccountName = null,
    bool? OnPremisesSyncEnabled = null);
public sealed record EmailMessage(string To, string Subject, string Body, IReadOnlyList<string>? Cc = null);
public sealed record ScriptExecutionResult(int ExitCode, string StandardOutput, string StandardError, string? LogPath = null);
