# Shared module bootstrap/repair for LeaverProcess.ps1 and readiness checks.

# Prefers repo-local .psmodules (outside OneDrive) to avoid corrupted EXO installs.



$script:LeaversFormGraphModuleNames = @(

    'Microsoft.Graph.Authentication'

    'Microsoft.Graph.Users'

    'Microsoft.Graph.Users.Actions'

    'Microsoft.Graph.Groups'

    'Microsoft.Graph.Identity.DirectoryManagement'

    'Microsoft.Graph.Applications'

)



$script:LeaversFormGraphModuleVersion = [version]'2.38.1'

$script:LeaversFormExchangeModuleVersion = [version]'3.5.1'



function Get-LeaversFormLocalModuleRoot {

    param([string]$BootstrapPath = $PSScriptRoot)



    $scriptsDir = if (Test-Path (Join-Path $BootstrapPath 'LeaverProcess.ps1')) {

        $BootstrapPath

    }

    else {

        Split-Path $BootstrapPath -Parent

    }



    Join-Path (Split-Path $scriptsDir -Parent) '.psmodules'

}



function Initialize-LeaversFormModulePath {

    param([string]$BootstrapPath = $PSScriptRoot)



    $localRoot = Get-LeaversFormLocalModuleRoot -BootstrapPath $BootstrapPath

    if (-not (Test-Path $localRoot)) {

        return $localRoot

    }



    if ($env:PSModulePath -notlike "*$localRoot*") {

        $env:PSModulePath = "$localRoot$([IO.Path]::PathSeparator)$env:PSModulePath"

    }



    return $localRoot

}



function Get-LeaversFormModuleScope {

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()

    $principal = New-Object Security.Principal.WindowsPrincipal($identity)

    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {

        return 'AllUsers'

    }

    return 'CurrentUser'

}



function Test-LeaversFormModuleConflictError {

    param([object]$ErrorRecord)



    $text = if ($ErrorRecord -is [System.Management.Automation.ErrorRecord]) {

        $ErrorRecord.Exception.Message

    }

    else {

        [string]$ErrorRecord

    }



    return $text -match 'Assembly with same name is already loaded|module could not be loaded|Could not load file or assembly|Module could not be correctly formed|Could not load type'

}



function Write-LeaversFormBootstrapLog {

    param(

        [string]$Message,

        [ValidateSet('INFO', 'WARN', 'ERROR', 'SUCCESS')]

        [string]$Level = 'INFO',

        [scriptblock]$Logger

    )



    if ($Logger) {

        & $Logger $Message $Level

    }

    else {

        Write-Host $Message

    }

}



function Get-LeaversFormGraphModuleVersion {

    Initialize-LeaversFormModulePath | Out-Null



    $authVersions = @(Get-Module -ListAvailable -Name 'Microsoft.Graph.Authentication' | ForEach-Object { $_.Version })

    if ($authVersions.Count -eq 0) {

        return $null

    }



    $shared = $authVersions | Sort-Object -Descending | Where-Object {

        $version = $_

        ($script:LeaversFormGraphModuleNames | ForEach-Object {

            Get-Module -ListAvailable -Name $_ | Where-Object { $_.Version -eq $version }

        } | Where-Object { $null -ne $_ }).Count -eq $script:LeaversFormGraphModuleNames.Count

    } | Select-Object -First 1



    if ($shared) { return $shared }



    return ($authVersions | Sort-Object -Descending | Select-Object -First 1)

}



function Install-LeaversFormLocalModules {

    param(

        [scriptblock]$Logger

    )



    $localRoot = Initialize-LeaversFormModulePath

    New-Item -ItemType Directory -Path $localRoot -Force | Out-Null



    if (-not (Get-PSRepository -Name 'PSGallery' -ErrorAction SilentlyContinue)) {

        Register-PSRepository -Default -ErrorAction SilentlyContinue

    }

    Set-PSRepository -Name 'PSGallery' -InstallationPolicy Trusted -ErrorAction SilentlyContinue



    Write-LeaversFormBootstrapLog "Installing modules to $localRoot (outside OneDrive)..." 'INFO' $Logger



    $exchangePath = Join-Path $localRoot "ExchangeOnlineManagement\$($script:LeaversFormExchangeModuleVersion)"

    if (-not (Test-Path $exchangePath)) {

        Write-LeaversFormBootstrapLog "Downloading ExchangeOnlineManagement $($script:LeaversFormExchangeModuleVersion)..." 'INFO' $Logger

        Save-Module 'ExchangeOnlineManagement' -RequiredVersion $script:LeaversFormExchangeModuleVersion -Path $localRoot -Force -ErrorAction Stop

    }



    foreach ($name in $script:LeaversFormGraphModuleNames) {

        $graphPath = Join-Path $localRoot "$name\$($script:LeaversFormGraphModuleVersion)"

        if (-not (Test-Path $graphPath)) {

            Write-LeaversFormBootstrapLog "Downloading $name $($script:LeaversFormGraphModuleVersion)..." 'INFO' $Logger

            Save-Module $name -RequiredVersion $script:LeaversFormGraphModuleVersion -Path $localRoot -Force -ErrorAction Stop

        }

    }

}



function Repair-LeaversFormGraphModules {

    param(

        [scriptblock]$Logger

    )



    Install-LeaversFormLocalModules -Logger $Logger

    Write-LeaversFormBootstrapLog "Graph modules aligned to version $($script:LeaversFormGraphModuleVersion)" 'INFO' $Logger

}



function Repair-LeaversFormExchangeModule {

    param(

        [scriptblock]$Logger

    )



    Install-LeaversFormLocalModules -Logger $Logger

    Write-LeaversFormBootstrapLog "ExchangeOnlineManagement aligned to version $($script:LeaversFormExchangeModuleVersion)" 'INFO' $Logger

}



function Clear-LeaversFormGraphModules {

    Get-Module -Name 'Microsoft.Graph.*' -ErrorAction SilentlyContinue | Remove-Module -Force -ErrorAction SilentlyContinue

}



function Clear-LeaversFormSessionModules {

    Clear-LeaversFormGraphModules

    Get-Module -Name 'ExchangeOnlineManagement' -ErrorAction SilentlyContinue | Remove-Module -Force -ErrorAction SilentlyContinue

}



function Clear-LeaversFormExchangeSession {

    param(

        [scriptblock]$Logger

    )



    try {

        Disconnect-ExchangeOnline -Confirm:$false -ErrorAction SilentlyContinue

    }

    catch {

        Write-LeaversFormBootstrapLog "Exchange disconnect warning: $($_.Exception.Message)" 'WARN' $Logger

    }



    Get-Module -Name 'ExchangeOnlineManagement' -ErrorAction SilentlyContinue |

        Remove-Module -Force -ErrorAction SilentlyContinue



    $script:ExchangeConnected = $false

}



function Import-LeaversFormGraphModules {

    param(

        [scriptblock]$Logger

    )



    Clear-LeaversFormGraphModules



    $version = Get-LeaversFormGraphModuleVersion

    if (-not $version) {

        Repair-LeaversFormGraphModules -Logger $Logger

        $version = Get-LeaversFormGraphModuleVersion

    }



    if (-not $version) {

        throw 'Microsoft Graph modules are not installed.'

    }



    Write-LeaversFormBootstrapLog "Loading Microsoft Graph modules (version $version)..." 'INFO' $Logger



    foreach ($name in $script:LeaversFormGraphModuleNames) {

        Import-Module $name -RequiredVersion $version -Force -ErrorAction Stop

    }

}



function Import-LeaversFormExchangeModule {

    param(

        [scriptblock]$Logger,

        [switch]$AllowRepair

    )



    Get-Module -Name 'ExchangeOnlineManagement' -ErrorAction SilentlyContinue |

        Remove-Module -Force -ErrorAction SilentlyContinue



    try {

        Import-Module 'ExchangeOnlineManagement' -RequiredVersion $script:LeaversFormExchangeModuleVersion -Force -ErrorAction Stop

        Get-Command 'Connect-ExchangeOnline' -ErrorAction Stop | Out-Null

        Write-LeaversFormBootstrapLog 'ExchangeOnlineManagement loaded.' 'SUCCESS' $Logger

    }

    catch {

        if (-not $AllowRepair) { throw }



        Write-LeaversFormBootstrapLog "Exchange module load failed: $($_.Exception.Message)" 'WARN' $Logger

        Repair-LeaversFormExchangeModule -Logger $Logger

        Import-Module 'ExchangeOnlineManagement' -RequiredVersion $script:LeaversFormExchangeModuleVersion -Force -ErrorAction Stop

        Get-Command 'Connect-ExchangeOnline' -ErrorAction Stop | Out-Null

        Write-LeaversFormBootstrapLog 'ExchangeOnlineManagement loaded after repair.' 'SUCCESS' $Logger

    }

}



function Initialize-LeaversFormRequiredModules {

    param(

        [scriptblock]$Logger,

        [ref]$RelaunchRequested,

        [switch]$DeferGraphLoad

    )



    Initialize-LeaversFormModulePath | Out-Null



    $writeLog = {

        param($Message, $Level = 'INFO')

        Write-LeaversFormBootstrapLog -Message $Message -Level $Level -Logger $Logger

    }



    try {

        Import-Module 'ActiveDirectory' -UseWindowsPowerShell -Force -ErrorAction Stop

        Get-Command 'Get-ADUser', 'Disable-ADAccount', 'Set-ADAccountPassword', 'Remove-ADGroupMember' -ErrorAction Stop | Out-Null



        if (-not $DeferGraphLoad) {

            Import-LeaversFormGraphModules -Logger $Logger

            Get-Command 'Connect-MgGraph', 'Get-MgUser', 'Update-MgUser', 'Get-MgGroup', 'Set-MgUserLicense', 'Revoke-MgUserSignInSession' -ErrorAction Stop | Out-Null

        }



        if ($DeferGraphLoad) {

            & $writeLog 'Core modules loaded (AD + deferred Graph — Exchange connects before Graph).' 'SUCCESS'

        }

        else {

            & $writeLog 'Required modules loaded successfully.' 'SUCCESS'

        }

    }

    catch {

        if (-not (Test-LeaversFormModuleConflictError $_)) { throw }



        & $writeLog "Module load error: $($_.Exception.Message)" 'WARN'

        & $writeLog 'Attempting automatic Graph module repair...' 'WARN'



        Repair-LeaversFormGraphModules -Logger $Logger



        try {

            Import-Module 'ActiveDirectory' -UseWindowsPowerShell -Force -ErrorAction Stop

            if (-not $DeferGraphLoad) {

                Import-LeaversFormGraphModules -Logger $Logger

                Get-Command 'Get-ADUser', 'Disable-ADAccount', 'Set-ADAccountPassword', 'Remove-ADGroupMember' -ErrorAction Stop | Out-Null

                Get-Command 'Connect-MgGraph', 'Get-MgUser', 'Update-MgUser', 'Get-MgGroup', 'Set-MgUserLicense', 'Revoke-MgUserSignInSession' -ErrorAction Stop | Out-Null

            }

            & $writeLog 'Required modules loaded successfully after repair.' 'SUCCESS'

        }

        catch {

            if (-not (Test-LeaversFormModuleConflictError $_)) { throw }



            & $writeLog 'In-process repair was not enough; a fresh PowerShell session is required.' 'WARN'

            $RelaunchRequested.Value = $true

            throw

        }

    }

}



function Complete-LeaversFormGraphModules {

    param(

        [scriptblock]$Logger

    )



    Import-LeaversFormGraphModules -Logger $Logger

    Get-Command 'Connect-MgGraph', 'Get-MgUser', 'Update-MgUser', 'Get-MgGroup', 'Set-MgUserLicense', 'Revoke-MgUserSignInSession' -ErrorAction Stop | Out-Null

    Write-LeaversFormBootstrapLog 'Microsoft Graph modules loaded for Entra actions.' 'SUCCESS' $Logger

}



function Test-LeaversFormModuleConnectivity {
    param(
        [string]$TenantId,
        [string]$AppId,
        [string]$Thumbprint,
        [string]$ExchangeOrganization
    )

    if (-not $TenantId -or -not $AppId -or -not $Thumbprint -or -not $ExchangeOrganization) {
        $settingsPath = Join-Path (Split-Path (Get-LeaversFormLocalModuleRoot) -Parent) 'LeaversForm.Web\appsettings.Development.local.json'
        if (Test-Path $settingsPath) {
            $json = Get-Content $settingsPath -Raw | ConvertFrom-Json
            if (-not $TenantId) { $TenantId = $json.LeaversForm.PowerShellTenantId ?? $json.AzureAd.TenantId }
            if (-not $AppId) { $AppId = $json.LeaversForm.PowerShellAppId }
            if (-not $Thumbprint) { $Thumbprint = $json.LeaversForm.PowerShellCertificateThumbprint }
            if (-not $ExchangeOrganization) { $ExchangeOrganization = $json.LeaversForm.PowerShellExchangeOrganization }
        }
    }

    if (-not $TenantId -or -not $AppId -or -not $Thumbprint -or -not $ExchangeOrganization) {
        Write-Host 'Skipping live connect test (could not resolve tenant/app/cert/org).' -ForegroundColor DarkGray
        return
    }

    $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
        Where-Object Thumbprint -eq $Thumbprint |
        Select-Object -First 1
    if (-not $cert) {
        throw "Certificate thumbprint '$Thumbprint' was not found for connectivity test."
    }

    try {
        Import-LeaversFormExchangeModule -AllowRepair
        $connectParams = @{
            AppId        = $AppId
            Organization = $ExchangeOrganization
            Certificate  = $cert
            ShowBanner   = $false
            ErrorAction  = 'Stop'
        }
        if ((Get-Command Connect-ExchangeOnline).Parameters.ContainsKey('DisableWAM')) {
            $connectParams.DisableWAM = $true
        }
        Connect-ExchangeOnline @connectParams
        Write-Host 'Exchange connect test: SUCCESS' -ForegroundColor Green
        Clear-LeaversFormExchangeSession

        Import-LeaversFormGraphModules
        Connect-MgGraph -TenantId $TenantId -ClientId $AppId -Certificate $cert -NoWelcome -ErrorAction Stop | Out-Null
        Write-Host 'Graph connect test: SUCCESS' -ForegroundColor Green
        Disconnect-MgGraph -ErrorAction SilentlyContinue | Out-Null
    }
    catch {
        Write-Host "Exchange connect test: FAILED - $($_.Exception.Message)" -ForegroundColor Red
        Write-Host 'Modules are installed — if connect still fails, complete Entra STEP 4 (Exchange service principal + Exchange.ManageAsApp).' -ForegroundColor Yellow
        throw
    }
}



function Restart-LeaverProcessInFreshSession {

    param(

        [string]$ScriptPath,

        [hashtable]$BoundParameters

    )



    $pwsh = (Get-Process -Id $PID).Path

    $args = @(

        '-NoProfile'

        '-ExecutionPolicy', 'Bypass'

        '-File', $ScriptPath

        '-LeaversFormInternalRelaunch'

    )



    foreach ($key in $BoundParameters.Keys) {

        if ($key -in @('LeaversFormInternalRelaunch', 'Verbose', 'Debug', 'ErrorAction', 'WarningAction', 'InformationAction', 'ProgressAction')) {

            continue

        }



        $value = $BoundParameters[$key]

        if ($value -is [switch]) {

            if ($value.IsPresent) { $args += "-$key" }

            continue

        }



        $args += "-$key"

        $args += [string]$value

    }



    Write-Host "Restarting leaver script in a fresh PowerShell session..."

    $process = Start-Process -FilePath $pwsh -ArgumentList $args -Wait -PassThru -NoNewWindow

    exit $process.ExitCode

}

