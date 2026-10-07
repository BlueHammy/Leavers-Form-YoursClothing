#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Allow outbound HTTPS from this server to Microsoft SSO and Graph endpoints.

.DESCRIPTION
  Creates Windows Firewall OUTBOUND allow rules for LeaversForm.
  Run on the IIS / Runner server during production setup.
  Adjust or mirror these rules on your corporate firewall if traffic is filtered upstream.

.EXAMPLE
  .\configure-firewall-outbound.ps1
  .\configure-firewall-outbound.ps1 -Remove
#>
[CmdletBinding()]
param(
    [switch] $Remove
)

$RuleGroup = "LeaversForm Production"
$Endpoints = @(
    @{ Name = "LeaversForm-Outbound-Microsoft-Login"; RemoteAddress = "login.microsoftonline.com"; Description = "Entra ID sign-in and token endpoints" },
    @{ Name = "LeaversForm-Outbound-Microsoft-Graph"; RemoteAddress = "graph.microsoft.com"; Description = "Microsoft Graph user search and mail" },
    @{ Name = "LeaversForm-Outbound-Microsoft-LoginCom"; RemoteAddress = "login.microsoft.com"; Description = "Entra ID auxiliary login endpoints" }
)

function Remove-Rules {
    foreach ($endpoint in $Endpoints) {
        $existing = Get-NetFirewallRule -DisplayName $endpoint.Name -ErrorAction SilentlyContinue
        if ($existing) {
            Remove-NetFirewallRule -DisplayName $endpoint.Name
            Write-Host "Removed rule: $($endpoint.Name)"
        }
    }
}

if ($Remove) {
    Remove-Rules
    exit 0
}

foreach ($endpoint in $Endpoints) {
    $existing = Get-NetFirewallRule -DisplayName $endpoint.Name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "Rule already exists: $($endpoint.Name)"
        continue
    }

    New-NetFirewallRule `
        -DisplayName $endpoint.Name `
        -Description $endpoint.Description `
        -Group $RuleGroup `
        -Direction Outbound `
        -Action Allow `
        -Protocol TCP `
        -RemotePort 443 `
        -RemoteAddress $endpoint.RemoteAddress `
        -Profile Domain, Private `
        | Out-Null

    Write-Host "Created outbound allow rule: $($endpoint.Name) -> $($endpoint.RemoteAddress):443"
}

Write-Host ""
Write-Host "Verify connectivity:"
Write-Host "  Test-NetConnection login.microsoftonline.com -Port 443"
Write-Host "  Test-NetConnection graph.microsoft.com -Port 443"
