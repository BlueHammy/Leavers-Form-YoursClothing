<#
.SYNOPSIS
    Full user offboarding script for hybrid AD / Microsoft 365 environments.

.DESCRIPTION
    Uses certificate/app-only authentication for Microsoft Graph and Exchange Online.

    - Sets mailbox automatic reply
    - Removes Exchange Online distribution group membership
    - Removes all mailbox/delegate/shared-mailbox permissions (Full Access, Send As, Send on Behalf)
    - Removes mailbox forwarding and hidden-from-GAL settings where applicable
    - Removes Microsoft 365 / Teams / SharePoint-backed group membership
    - Removes Entra directory role memberships and enterprise app role assignments
    - Removes Microsoft 365 licences
    - Revokes cloud sign-in sessions
    - Disables Entra ID account
    - Clears AD dial-in setting to "Control access through NPS Network Policy"
    - Removes on-prem AD group membership except primary group
    - Resets on-prem AD password to long random value
    - Disables on-prem AD account
    - Optionally deletes on-prem AD account

.NOTES
    Run in PowerShell 7 as admin.
    Test with -WhatIf first.

    Cloud auth:
        Microsoft Graph: App-only certificate auth
        Exchange Online: App-only certificate auth

    On-prem AD:
        Runs as the Windows identity executing the script.
        For scheduled tasks, use a dedicated domain service account with delegated AD permissions.
#>

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)]
    [string]$UserPrincipalName,

    [Parameter(Mandatory = $false)]
    [string]$SamAccountName,

    [Parameter(Mandatory = $false)]
    [string]$OutOfOfficeMessage = "This Mailbox is no longer active.",

    [Parameter(Mandatory = $false)]
    [string]$LogPath = "C:\Scripts\Logs\Offboarding-$((Get-Date).ToString('yyyyMMdd-HHmmss')).log",

    [Parameter(Mandatory = $true)]
    [string]$TenantId = $env:LEAVERSFORM_PS_TENANT_ID,

    [Parameter(Mandatory = $true)]
    [string]$AppId = $env:LEAVERSFORM_PS_APP_ID,

    [Parameter(Mandatory = $true)]
    [string]$CertificateThumbprint = $env:LEAVERSFORM_PS_CERTIFICATE_THUMBPRINT,

    [Parameter(Mandatory = $true)]
    [string]$ExchangeOrganization = $env:LEAVERSFORM_PS_EXCHANGE_ORGANIZATION,

    [Parameter(Mandatory = $false)]
    [string]$DomainController = $env:LEAVERSFORM_PS_DOMAIN_CONTROLLER,

    [Parameter(Mandatory = $false)]
    [switch]$LeaversFormInternalRelaunch,

    [Parameter(Mandatory = $false)]
    [int]$MaxRuntimeMinutes = 15,

    [Parameter(Mandatory = $false)]
    [switch]$SandboxSkipAdDisable
)

# =====================================
# App Registration Configuration
# =====================================

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$script:FailureCount = 0
$script:EntraDisabled = $false
$script:AdDisabled = $false
$script:Deadline = (Get-Date).AddMinutes($MaxRuntimeMinutes)

function Test-ScriptDeadline {
    if ((Get-Date) -gt $script:Deadline) {
        throw "Script runtime limit of $MaxRuntimeMinutes minute(s) exceeded."
    }
}

foreach ($required in @(
    @{ Name = "TenantId"; Value = $TenantId },
    @{ Name = "AppId"; Value = $AppId },
    @{ Name = "CertificateThumbprint"; Value = $CertificateThumbprint },
    @{ Name = "ExchangeOrganization"; Value = $ExchangeOrganization }
)) {
    if ([string]::IsNullOrWhiteSpace($required.Value)) {
        throw "$($required.Name) is required. Supply it as a script parameter or LEAVERSFORM_PS_* environment variable."
    }
}

# -----------------------------
# Logging setup
# -----------------------------

$LogDirectory = Split-Path -Path $LogPath -Parent

if ($LogDirectory -and -not (Test-Path -Path $LogDirectory)) {
    New-Item -Path $LogDirectory -ItemType Directory -Force | Out-Null
}

function Write-Log {
    param(
        [string]$Message,

        [ValidateSet("INFO", "WARN", "ERROR", "SUCCESS")]
        [string]$Level = "INFO"
    )

    $line = "{0} [{1}] {2}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Level, $Message
    Write-Host $line
    Add-Content -Path $LogPath -Value $line
}

function Invoke-Safe {
    param(
        [string]$Action,
        [scriptblock]$ScriptBlock
    )

    try {
        Test-ScriptDeadline
        Write-Log "Starting: $Action"
        Invoke-SafeCore $ScriptBlock
        Write-Log "Completed: $Action" "SUCCESS"
    }
    catch {
        Write-Log "Failed: $Action. $($_.Exception.Message)" "ERROR"
        $script:FailureCount++
    }
}

function Invoke-SafeCore {
    param([scriptblock]$ScriptBlock)

    # Nested function prevents `return` inside $ScriptBlock from terminating the whole script file.
    function Invoke-SafeStep {
        param([scriptblock]$InnerBlock)
        & $InnerBlock
    }

    Invoke-SafeStep $ScriptBlock
}

function Invoke-SafeRecoverable {
    param(
        [string]$Action,
        [scriptblock]$ScriptBlock,
        [scriptblock]$RepairAction,
        [int]$MaxRetries = 2
    )

    for ($attempt = 0; $attempt -le $MaxRetries; $attempt++) {
        try {
            Test-ScriptDeadline
            $suffix = if ($attempt -gt 0) { " (auto-retry $attempt)" } else { "" }
            Write-Log "Starting: $Action$suffix"
            Invoke-SafeCore $ScriptBlock
            Write-Log "Completed: $Action" "SUCCESS"
            return
        }
        catch {
            $message = $_.Exception.Message
            $recoverable = $message -match 'Module could not|Assembly with same name|Could not load file or assembly|Could not load type|429|503|temporarily unavailable|timed out|timeout|forcibly closed|connection attempt failed|Service Unavailable'

            if ($recoverable -and $attempt -lt $MaxRetries) {
                Write-Log "Recoverable error during '$Action' — repairing and retrying: $message" "WARN"
                if ($RepairAction) {
                    & $RepairAction
                }
                continue
            }

            Write-Log "Failed: $Action. $message" "ERROR"
            $script:FailureCount++
            return
        }
    }
}

function Repair-LeaverProcessEnvironment {
    param([switch]$FullRepair)

    Write-Log "Running in-script environment repair (full=$FullRepair)..." "WARN"
    Clear-LeaversFormGraphModules

    if (-not $FullRepair) {
        return
    }

    try {
        Repair-LeaversFormGraphModules -Logger ${function:Write-Log}
    }
    catch {
        Write-Log "Graph module repair failed: $($_.Exception.Message)" "WARN"
    }

    Clear-LeaversFormGraphModules
}

function New-RandomSecurePassword {
    param(
        [int]$Length = 64
    )

    $upper   = "ABCDEFGHJKLMNPQRSTUVWXYZ"
    $lower   = "abcdefghijkmnopqrstuvwxyz"
    $numbers = "23456789"
    $symbols = "!@$%^-_=+[]{}:,.?"

    $allChars = ($upper + $lower + $numbers + $symbols).ToCharArray()

    $passwordChars = New-Object System.Collections.Generic.List[char]

    # Ensure complexity
    $passwordChars.Add($upper[(Get-Random -Minimum 0 -Maximum $upper.Length)])
    $passwordChars.Add($lower[(Get-Random -Minimum 0 -Maximum $lower.Length)])
    $passwordChars.Add($numbers[(Get-Random -Minimum 0 -Maximum $numbers.Length)])
    $passwordChars.Add($symbols[(Get-Random -Minimum 0 -Maximum $symbols.Length)])

    for ($i = $passwordChars.Count; $i -lt $Length; $i++) {
        $passwordChars.Add($allChars[(Get-Random -Minimum 0 -Maximum $allChars.Length)])
    }

    -join ($passwordChars | Sort-Object { Get-Random })
}

function Get-AutomationCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Thumbprint
    )

    $cleanThumbprint = ($Thumbprint -replace "\s", "").ToUpperInvariant()

    $cert = $null
    foreach ($storePath in @('Cert:\CurrentUser\My', 'Cert:\LocalMachine\My')) {
        if (-not (Test-Path $storePath)) { continue }
        $cert = Get-ChildItem -Path $storePath -ErrorAction SilentlyContinue |
            Where-Object { ($_.Thumbprint -replace "\s", "").ToUpperInvariant() -eq $cleanThumbprint } |
            Select-Object -First 1
        if ($cert) { break }
    }

    if (-not $cert) {
        $pfxPath = $env:LEAVERSFORM_PS_CERTIFICATE_PATH
        if ($pfxPath -and (Test-Path -LiteralPath $pfxPath)) {
            $password = $env:LEAVERSFORM_PS_CERTIFICATE_PASSWORD
            if ($password) {
                $secure = ConvertTo-SecureString $password -AsPlainText -Force
                $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxPath, $secure)
            }
            else {
                $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxPath)
            }
            $actual = ($cert.Thumbprint -replace "\s", "").ToUpperInvariant()
            if ($actual -ne $cleanThumbprint) {
                throw "Certificate at '$pfxPath' has thumbprint '$($cert.Thumbprint)', expected '$Thumbprint'."
            }
        }
    }

    if (-not $cert) {
        throw "Certificate with thumbprint '$Thumbprint' was not found in Cert:\CurrentUser\My, Cert:\LocalMachine\My, or LEAVERSFORM_PS_CERTIFICATE_PATH."
    }

    if (-not $cert.HasPrivateKey) {
        throw "Certificate '$($cert.Subject)' was found, but it does not have a private key. The private key is required for app-only authentication."
    }

    if ($cert.NotAfter -lt (Get-Date)) {
        throw "Certificate '$($cert.Subject)' expired on $($cert.NotAfter)."
    }

    return $cert
}

Write-Log "========== Starting offboarding for $UserPrincipalName =========="

$bootstrapPath = Join-Path $PSScriptRoot 'LeaversForm-ModuleBootstrap.ps1'
if (-not (Test-Path $bootstrapPath)) {
    $bootstrapPath = Join-Path (Split-Path -Parent $PSCommandPath) 'LeaversForm-ModuleBootstrap.ps1'
}
if (-not (Test-Path $bootstrapPath)) {
    throw "LeaversForm-ModuleBootstrap.ps1 was not found beside LeaverProcess.ps1."
}
. $bootstrapPath

# -----------------------------
# Module loading (auto-repair + fresh-session relaunch if needed)
# -----------------------------

$relaunchRequested = $false
try {
    Initialize-LeaversFormRequiredModules -Logger ${function:Write-Log} -RelaunchRequested ([ref]$relaunchRequested) -DeferGraphLoad
}
catch {
    if ($relaunchRequested -and -not $LeaversFormInternalRelaunch) {
        Restart-LeaverProcessInFreshSession -ScriptPath $PSCommandPath -BoundParameters $PSBoundParameters
    }

    Write-Log "Fatal: required modules could not be loaded. $($_.Exception.Message)" "ERROR"
    throw
}

$script:ExchangeConnected = $false

function Test-ExchangeReady {
    return [bool]$script:ExchangeConnected
}

function Get-GroupCategoryLabel {
    param($Group)

    $labels = New-Object System.Collections.Generic.List[string]
    if ($Group.GroupTypes -contains 'Unified') { $labels.Add('Teams/M365') }
    if ($Group.ResourceProvisioningOptions -contains 'Team') { $labels.Add('Teams') }
    if ($Group.ResourceProvisioningOptions -contains 'SharePoint') { $labels.Add('SharePoint') }
    if ($Group.MailEnabled -and -not ($Group.GroupTypes -contains 'Unified')) { $labels.Add('Mail-enabled') }
    if ($Group.SecurityEnabled) { $labels.Add('Security') }

    if ($labels.Count -eq 0) { return 'Group' }
    return ($labels | Select-Object -Unique) -join '/'
}

# -----------------------------
# Load certificate
# -----------------------------

$AutomationCertificate = $null

Invoke-Safe "Load certificate for app-only authentication" {
    $script:AutomationCertificate = Get-AutomationCertificate -Thumbprint $CertificateThumbprint

    Write-Log "Loaded certificate: $($script:AutomationCertificate.Subject)"
    Write-Log "Certificate thumbprint: $($script:AutomationCertificate.Thumbprint)"
    Write-Log "Certificate expires: $($script:AutomationCertificate.NotAfter)" "SUCCESS"
}

# -----------------------------
# Connect to Exchange Online FIRST (before Graph — avoids module clash; mailbox work before licence removal)
# -----------------------------

Invoke-SafeRecoverable "Connect to Exchange Online using app-only certificate authentication" {
    Import-LeaversFormExchangeModule -Logger ${function:Write-Log} -AllowRepair

    $exoParams = @{
        AppId         = $AppId
        Organization  = $ExchangeOrganization
        ShowBanner    = $false
        ErrorAction   = 'Stop'
    }

    if ((Get-Command Connect-ExchangeOnline).Parameters.ContainsKey('DisableWAM')) {
        $exoParams.DisableWAM = $true
    }

    try {
        Connect-ExchangeOnline @exoParams -Certificate $AutomationCertificate
    }
    catch {
        Write-Log "Exchange connect with certificate object failed: $($_.Exception.Message). Retrying with thumbprint..." "WARN"
        Connect-ExchangeOnline @exoParams -CertificateThumbprint $AutomationCertificate.Thumbprint
    }

    $script:ExchangeConnected = $true
    Write-Log "Connected to Exchange Online using app-only certificate authentication." "SUCCESS"
} -RepairAction { Repair-LeaversFormExchangeModule -Logger ${function:Write-Log} } -MaxRetries 1

# -----------------------------
# Resolve mailbox + AD (Entra resolved after Exchange mailbox work)
# -----------------------------

$MgUser = $null
$AdUser = $null
$ExoMailbox = $null
$ExoRecipient = $null

Invoke-Safe "Resolve Exchange recipient/mailbox" {
    if (-not $script:ExchangeConnected) {
        throw "Exchange Online is not connected — skipping mailbox resolution."
    }

    $script:ExoRecipient = Get-Recipient -Identity $UserPrincipalName -ErrorAction Stop
    $script:ExoMailbox = Get-Mailbox -Identity $UserPrincipalName -ErrorAction Stop

    Write-Log "Resolved Exchange mailbox: $($script:ExoMailbox.PrimarySmtpAddress)" "SUCCESS"
}

Invoke-Safe "Resolve on-prem AD user" {
    $computerSystem = Get-CimInstance Win32_ComputerSystem
    Write-Log "Computer domain joined: $($computerSystem.PartOfDomain) (domain: $($computerSystem.Domain))"

    if ($DomainController) {
        $reachable = Test-Connection -ComputerName $DomainController -Count 1 -Quiet -ErrorAction SilentlyContinue
        Write-Log "Ping domain controller '$DomainController': $(if ($reachable) { 'OK' } else { 'FAILED — check VPN, DNS, or update LeaversForm:PowerShellDomainController' })"
        if (-not $reachable) {
            throw "Cannot reach domain controller '$DomainController'. Connect to VPN and verify the DC name in appsettings."
        }
    }

    $adParams = @{
        Properties  = @(
            "MemberOf",
            "PrimaryGroupID",
            "DistinguishedName",
            "mail",
            "msNPAllowDialin",
            "Enabled"
        )
        ErrorAction = "Stop"
    }

    if ($DomainController) {
        $adParams.Server = $DomainController
    }

    if ($SamAccountName) {
        $script:AdUser = Get-ADUser -Identity $SamAccountName @adParams
    }
    else {
        $escapedUpn = $UserPrincipalName.Replace("'", "''")
        $script:AdUser = Get-ADUser -Filter "UserPrincipalName -eq '$escapedUpn'" @adParams
    }

    Write-Log "Resolved AD user: $($script:AdUser.SamAccountName) / $($script:AdUser.DistinguishedName)" "SUCCESS"
}

$GeneratedLeaverPassword = New-RandomSecurePassword -Length 64
Write-Log "Generated random leaver password. Password is intentionally not written to log." "SUCCESS"

# -----------------------------
# Exchange Online mailbox actions
# -----------------------------

if (-not (Test-ExchangeReady)) {
    Write-Log "Skipping all Exchange Online mailbox actions — Exchange Online is not connected." "WARN"
}
else {

Invoke-Safe "Set mailbox automatic replies" {
    if ($PSCmdlet.ShouldProcess($UserPrincipalName, "Set automatic reply")) {
        Set-MailboxAutoReplyConfiguration `
            -Identity $UserPrincipalName `
            -AutoReplyState Enabled `
            -InternalMessage $OutOfOfficeMessage `
            -ExternalMessage $OutOfOfficeMessage `
            -ExternalAudience All `
            -ErrorAction Stop

        Write-Log "Automatic replies enabled for: $UserPrincipalName" "SUCCESS"
    }
}

Invoke-Safe "Remove mailbox forwarding and alternate recipients" {
    if (-not $ExoMailbox) {
        throw "Exchange mailbox not resolved."
    }

    if ($PSCmdlet.ShouldProcess($UserPrincipalName, "Clear mailbox forwarding")) {
        Set-Mailbox `
            -Identity $UserPrincipalName `
            -ForwardingAddress $null `
            -ForwardingSmtpAddress $null `
            -DeliverToMailboxAndForward $false `
            -ErrorAction SilentlyContinue

        Write-Log "Cleared mailbox forwarding settings for: $UserPrincipalName" "SUCCESS"
    }
}

Invoke-Safe "Remove from Exchange distribution groups and mail-enabled security groups" {
    if (-not $ExoRecipient) {
        throw "Exchange recipient not resolved."
    }

    $recipientDn = $ExoRecipient.DistinguishedName

    $groups = Get-DistributionGroup `
        -ResultSize Unlimited `
        -Filter "Members -eq '$recipientDn'" `
        -ErrorAction SilentlyContinue

    foreach ($group in $groups) {
        if ($PSCmdlet.ShouldProcess($group.DisplayName, "Remove $UserPrincipalName from distribution/mail-enabled group")) {
            try {
                Remove-DistributionGroupMember `
                    -Identity $group.Identity `
                    -Member $UserPrincipalName `
                    -Confirm:$false `
                    -ErrorAction Stop

                Write-Log "Removed from Exchange group: $($group.DisplayName)" "SUCCESS"
            }
            catch {
                Write-Log "Could not remove from Exchange group $($group.DisplayName): $($_.Exception.Message)" "WARN"
            }
        }
    }
}

Invoke-Safe "Remove all mailbox Full Access permissions granted to leaver" {
    $permissions = Get-MailboxPermission -User $UserPrincipalName -ErrorAction SilentlyContinue |
        Where-Object {
            $_.AccessRights -contains "FullAccess" -and
            $_.IsInherited -eq $false -and
            $_.User -notmatch "NT AUTHORITY\\SYSTEM"
        }

    foreach ($perm in $permissions) {
        if ($PSCmdlet.ShouldProcess($perm.Identity, "Remove Full Access for $UserPrincipalName")) {
            Remove-MailboxPermission `
                -Identity $perm.Identity `
                -User $UserPrincipalName `
                -AccessRights FullAccess `
                -Confirm:$false `
                -ErrorAction SilentlyContinue

            Write-Log "Removed Full Access from mailbox: $($perm.Identity)" "SUCCESS"
        }
    }
}

Invoke-Safe "Remove shared mailbox Send As permissions" {
    $recipientPerms = Get-RecipientPermission `
        -Trustee $UserPrincipalName `
        -ResultSize Unlimited `
        -ErrorAction SilentlyContinue |
        Where-Object {
            $_.AccessRights -contains "SendAs" -and
            $_.IsInherited -eq $false
        }

    foreach ($perm in $recipientPerms) {
        if ($PSCmdlet.ShouldProcess($perm.Identity, "Remove Send As for $UserPrincipalName")) {
            Remove-RecipientPermission `
                -Identity $perm.Identity `
                -Trustee $UserPrincipalName `
                -AccessRights SendAs `
                -Confirm:$false `
                -ErrorAction SilentlyContinue

            Write-Log "Removed Send As from: $($perm.Identity)" "SUCCESS"
        }
    }
}

Invoke-Safe "Remove Send on Behalf permissions on shared mailboxes" {
    $sharedMailboxes = Get-Mailbox -RecipientTypeDetails SharedMailbox -ResultSize Unlimited -ErrorAction SilentlyContinue

    foreach ($mailbox in $sharedMailboxes) {
        try {
            $sobList = @($mailbox.GrantSendOnBehalfTo)
            if (-not $sobList -or $sobList.Count -eq 0) { continue }

            $matched = $false
            foreach ($entry in $sobList) {
                $entryText = [string]$entry
                if (
                    $entryText -ieq $UserPrincipalName -or
                    $entryText -like "*$UserPrincipalName*" -or
                    ($ExoRecipient -and $entryText -like "*$($ExoRecipient.DisplayName)*") -or
                    ($ExoRecipient -and $entryText -like "*$($ExoRecipient.Name)*")
                ) {
                    $matched = $true
                    break
                }
            }

            if ($matched) {
                if ($PSCmdlet.ShouldProcess($mailbox.PrimarySmtpAddress, "Remove Send on Behalf for $UserPrincipalName")) {
                    Set-Mailbox `
                        -Identity $mailbox.Identity `
                        -GrantSendOnBehalfTo @{ Remove = $UserPrincipalName } `
                        -ErrorAction Stop

                    Write-Log "Removed Send on Behalf from mailbox: $($mailbox.PrimarySmtpAddress)" "SUCCESS"
                }
            }
        }
        catch {
            Write-Log "Could not process Send on Behalf for $($mailbox.PrimarySmtpAddress): $($_.Exception.Message)" "WARN"
        }
    }
}

}

Clear-LeaversFormExchangeSession -Logger ${function:Write-Log}
Write-Log "Exchange session closed before loading Graph for Entra actions." "INFO"

# -----------------------------
# Load Graph for Entra actions (after Exchange mailbox work — licence removal comes later)
# -----------------------------

Invoke-SafeRecoverable "Connect to Microsoft Graph using app-only certificate authentication" {
    Complete-LeaversFormGraphModules -Logger ${function:Write-Log}

    Connect-MgGraph `
        -TenantId $TenantId `
        -ClientId $AppId `
        -Certificate $AutomationCertificate `
        -NoWelcome `
        -ErrorAction Stop

    $context = Get-MgContext

    if ($context.AuthType -ne "AppOnly") {
        throw "Graph connection did not authenticate as AppOnly. Current auth type: $($context.AuthType)"
    }

    Write-Log "Connected to Microsoft Graph as app-only. AppId: $($context.ClientId)" "SUCCESS"
} -RepairAction { Repair-LeaverProcessEnvironment } -MaxRetries 1

Invoke-Safe "Resolve Entra ID user" {
    $script:MgUser = Get-MgUser `
        -UserId $UserPrincipalName `
        -Property Id,DisplayName,UserPrincipalName,AccountEnabled,AssignedLicenses `
        -ErrorAction Stop

    Write-Log "Resolved Entra user: $($script:MgUser.DisplayName) / $($script:MgUser.Id)" "SUCCESS"
}

# -----------------------------
# Critical path: disable accounts before group/mailbox cleanup
# -----------------------------

Write-Log "=== Critical phase: Entra + AD disable before group cleanup ===" "INFO"

Invoke-Safe "Revoke Microsoft 365 sign-in sessions" {
    if (-not $MgUser) {
        throw "Graph user not resolved."
    }

    if ($PSCmdlet.ShouldProcess($UserPrincipalName, "Revoke sign-in sessions")) {
        Revoke-MgUserSignInSession -UserId $MgUser.Id -ErrorAction Stop
        Write-Log "Revoked Microsoft 365 sign-in sessions for: $UserPrincipalName" "SUCCESS"
    }
}

Invoke-Safe "Disable Entra ID account" {
    if (-not $MgUser) {
        throw "Graph user not resolved."
    }

    $freshUser = Get-MgUser -UserId $MgUser.Id -Property AccountEnabled -ErrorAction Stop
    if (-not $freshUser.AccountEnabled) {
        $script:EntraDisabled = $true
        Write-Log "Entra ID account already disabled for: $UserPrincipalName" "SUCCESS"
    }
    elseif ($PSCmdlet.ShouldProcess($UserPrincipalName, "Disable Entra ID account")) {
        Update-MgUser `
            -UserId $MgUser.Id `
            -AccountEnabled:$false `
            -ErrorAction Stop

        $script:EntraDisabled = $true
        Write-Log "Disabled Entra ID account for: $UserPrincipalName" "SUCCESS"
    }
}

Invoke-Safe "Remove Microsoft 365 licences" {
    if (-not $MgUser) {
        throw "Graph user not resolved."
    }

    $freshUser = Get-MgUser -UserId $MgUser.Id -Property AssignedLicenses -ErrorAction Stop
    $skuIds = @($freshUser.AssignedLicenses | ForEach-Object { $_.SkuId })

    if ($skuIds.Count -eq 0) {
        Write-Log "No assigned licences found."
    }
    else {
        if ($PSCmdlet.ShouldProcess($UserPrincipalName, "Remove $($skuIds.Count) Microsoft 365 licence assignment(s)")) {
            Set-MgUserLicense `
                -UserId $MgUser.Id `
                -RemoveLicenses $skuIds `
                -AddLicenses @() `
                -ErrorAction Stop

            Write-Log "Removed $($skuIds.Count) licence assignment(s)." "SUCCESS"
        }
    }
}

# -----------------------------
# On-prem AD critical disable (before slow dial-in / group cleanup)
# -----------------------------

Invoke-Safe "Reset on-prem AD password to long random value" {
    if (-not $AdUser) {
        throw "AD user not resolved."
    }

    if ($SandboxSkipAdDisable) {
        $script:AdDisabled = $true
        Write-Log "SANDBOX: Skipped AD password reset and disable — account unchanged." "WARN"
    }
    else {
        $getParams = @{
            Identity   = $AdUser.DistinguishedName
            Properties = 'Enabled'
        }
        if ($DomainController) {
            $getParams.Server = $DomainController
        }
        $liveAdUser = Get-ADUser @getParams
        if (-not $liveAdUser.Enabled) {
            $script:AdDisabled = $true
            Write-Log "AD account already disabled: $($AdUser.SamAccountName)" "SUCCESS"
        }
        else {
            $securePassword = ConvertTo-SecureString $GeneratedLeaverPassword -AsPlainText -Force

            $params = @{
                Identity    = $AdUser.DistinguishedName
                NewPassword = $securePassword
                Reset       = $true
            }

            if ($DomainController) {
                $params.Server = $DomainController
            }

            if ($PSCmdlet.ShouldProcess($AdUser.SamAccountName, "Reset on-prem AD password to long random value")) {
                Set-ADAccountPassword @params
                Write-Log "Reset on-prem AD password for: $($AdUser.SamAccountName)" "SUCCESS"
            }
        }
    }
}

if (-not $SandboxSkipAdDisable) {
Invoke-Safe "Disable on-prem AD account" {
    if (-not $AdUser) {
        throw "AD user not resolved."
    }

    $getParams = @{
        Identity   = $AdUser.DistinguishedName
        Properties = 'Enabled'
    }
    if ($DomainController) {
        $getParams.Server = $DomainController
    }
    $liveAdUser = Get-ADUser @getParams
    if (-not $liveAdUser.Enabled) {
        $script:AdDisabled = $true
        Write-Log "AD account already disabled: $($AdUser.SamAccountName)" "SUCCESS"
    }
    else {
        $params = @{
            Identity = $AdUser.DistinguishedName
        }

        if ($DomainController) {
            $params.Server = $DomainController
        }

        if ($PSCmdlet.ShouldProcess($AdUser.SamAccountName, "Disable AD account")) {
            Disable-ADAccount @params
            $script:AdDisabled = $true
            Write-Log "Disabled AD account: $($AdUser.SamAccountName)" "SUCCESS"
        }
    }
}
}

Write-Log "=== Critical phase complete. Entra disabled=$($script:EntraDisabled), AD disabled=$($script:AdDisabled) ===" "INFO"

if (-not $SandboxSkipAdDisable) {
    Invoke-Safe "Set AD Dial-in to Control access through NPS Network Policy" {
        if (-not $AdUser) {
            throw "AD user not resolved."
        }

        $params = @{
            Identity = $AdUser.DistinguishedName
            Clear    = "msNPAllowDialin"
        }

        if ($DomainController) {
            $params.Server = $DomainController
        }

        if ($PSCmdlet.ShouldProcess($AdUser.SamAccountName, "Clear msNPAllowDialin")) {
            Set-ADUser @params
            Write-Log "Cleared msNPAllowDialin. Dial-in should now be controlled through NPS Network Policy." "SUCCESS"
        }
    }

    Invoke-Safe "Remove on-prem AD group memberships" {
        if (-not $AdUser) {
            throw "AD user not resolved."
        }

        $gpParams = @{
            Identity = $AdUser.DistinguishedName
        }

        if ($DomainController) {
            $gpParams.Server = $DomainController
        }

        $groupJob = Start-Job -ArgumentList $gpParams, $DomainController -ScriptBlock {
            param($MembershipParams, $Dc)
            Import-Module ActiveDirectory -ErrorAction Stop
            if ($Dc) {
                $MembershipParams.Server = $Dc
            }
            Get-ADPrincipalGroupMembership @MembershipParams
        }

        if (-not (Wait-Job $groupJob -Timeout 120)) {
            Stop-Job $groupJob -ErrorAction SilentlyContinue
            Remove-Job $groupJob -Force -ErrorAction SilentlyContinue
            throw "Timed out after 120 seconds listing AD group memberships."
        }

        $groups = @(Receive-Job $groupJob)
        Remove-Job $groupJob -Force -ErrorAction SilentlyContinue
        $groups = $groups | Where-Object { $_.Name -ne "Domain Users" }

        if (-not $groups) {
            Write-Log "No removable AD group memberships found."
        }
        else {
        foreach ($group in $groups) {
            Test-ScriptDeadline
            $removeParams = @{
                Identity = $group.DistinguishedName
                Members  = $AdUser.DistinguishedName
                Confirm  = $false
            }

            if ($DomainController) {
                $removeParams.Server = $DomainController
            }

            if ($PSCmdlet.ShouldProcess($group.Name, "Remove $($AdUser.SamAccountName) from AD group")) {
                try {
                    Remove-ADGroupMember @removeParams -ErrorAction Stop
                    Write-Log "Removed from AD group: $($group.Name)" "SUCCESS"
                }
                catch {
                    Write-Log "Could not remove from AD group $($group.Name): $($_.Exception.Message)" "WARN"
                }
            }
        }
        }
    }
}
else {
    Write-Log "SANDBOX: Skipping on-prem AD dial-in and group cleanup." "WARN"
}

# -----------------------------
# Non-critical cleanup (group/mailbox permissions)
# -----------------------------

if ($SandboxSkipAdDisable) {
    Write-Log "SANDBOX: Skipping Microsoft 365 group and role cleanup." "WARN"
}
else {

Invoke-Safe "Remove from Microsoft 365 groups, Teams, SharePoint-backed groups, and security groups" {
    if (-not $MgUser) {
        throw "Graph user not resolved."
    }

    $memberships = Get-MgUserMemberOf -UserId $MgUser.Id -All

    foreach ($object in $memberships) {
        Test-ScriptDeadline
        $odataType = $object.AdditionalProperties.'@odata.type'

        if ($odataType -ne "#microsoft.graph.group") {
            Write-Log "Skipping non-group directory object: $($object.Id)" "WARN"
            continue
        }

        $group = Get-MgGroup `
            -GroupId $object.Id `
            -Property Id,DisplayName,GroupTypes,SecurityEnabled,MailEnabled,MembershipRule,ResourceProvisioningOptions,OnPremisesSyncEnabled `
            -ErrorAction SilentlyContinue

        if (-not $group) {
            Write-Log "Could not resolve group object: $($object.Id)" "WARN"
            continue
        }

        if ($group.OnPremisesSyncEnabled) {
            Write-Log "Skipping on-premises synced group (change via AD): $($group.DisplayName)" "WARN"
            continue
        }

        if ($group.MembershipRule) {
            Write-Log "Skipping dynamic group because membership is rule-based: $($group.DisplayName)" "WARN"
            continue
        }

        $category = Get-GroupCategoryLabel -Group $group

        try {
            if ($PSCmdlet.ShouldProcess($group.DisplayName, "Remove group membership for $UserPrincipalName")) {
                Remove-MgGroupMemberByRef `
                    -GroupId $group.Id `
                    -DirectoryObjectId $MgUser.Id `
                    -ErrorAction Stop

                Write-Log "Removed from ${category} group: $($group.DisplayName)" "SUCCESS"
            }
        }
        catch {
            Write-Log "Could not remove from group $($group.DisplayName): $($_.Exception.Message)" "WARN"
        }
    }
}

Invoke-Safe "Remove Entra directory role memberships" {
    if (-not $MgUser) {
        throw "Graph user not resolved."
    }

    $roles = Get-MgDirectoryRole -All -ErrorAction SilentlyContinue
    foreach ($role in $roles) {
        Test-ScriptDeadline
        $members = Get-MgDirectoryRoleMember -DirectoryRoleId $role.Id -All -ErrorAction SilentlyContinue
        $match = $members | Where-Object { $_.Id -eq $MgUser.Id }
        if (-not $match) { continue }

        if ($PSCmdlet.ShouldProcess($role.DisplayName, "Remove directory role for $UserPrincipalName")) {
            Remove-MgDirectoryRoleMemberByRef `
                -DirectoryRoleId $role.Id `
                -DirectoryObjectId $MgUser.Id `
                -ErrorAction Stop

            Write-Log "Removed Entra directory role: $($role.DisplayName)" "SUCCESS"
        }
    }
}

Invoke-Safe "Remove enterprise application role assignments" {
    if (-not $MgUser) {
        throw "Graph user not resolved."
    }

    $assignments = Get-MgUserAppRoleAssignment -UserId $MgUser.Id -All -ErrorAction SilentlyContinue
    foreach ($assignment in $assignments) {
        Test-ScriptDeadline
        if ($PSCmdlet.ShouldProcess($assignment.Id, "Remove app role assignment for $UserPrincipalName")) {
            Remove-MgUserAppRoleAssignment `
                -AppRoleAssignmentId $assignment.Id `
                -UserId $MgUser.Id `
                -ErrorAction Stop

            Write-Log "Removed enterprise app role assignment: $($assignment.ResourceDisplayName)" "SUCCESS"
        }
    }
}


}

# -----------------------------
# Disconnect
# -----------------------------

Invoke-Safe "Disconnect sessions" {
    try {
        Disconnect-ExchangeOnline -Confirm:$false -ErrorAction SilentlyContinue
    }
    catch {}

    try {
        Disconnect-MgGraph -ErrorAction SilentlyContinue
    }
    catch {}
}

Write-Log "========== Finished offboarding for $UserPrincipalName =========="
Write-Log "Log saved to: $LogPath"
Write-Log "Core outcomes: Entra disabled=$($script:EntraDisabled), AD disabled=$($script:AdDisabled), non-critical failures=$($script:FailureCount)"

if (-not $script:EntraDisabled -or ((-not $script:AdDisabled) -and -not $SandboxSkipAdDisable)) {
    Write-Log "Offboarding failed because the Entra and/or AD account was not disabled." "ERROR"
    exit 1
}

if ($script:FailureCount -gt 0) {
    Write-Log "Offboarding completed with $($script:FailureCount) failed action(s)." "ERROR"
    exit 1
}

exit 0