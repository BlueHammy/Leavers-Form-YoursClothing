# Production deployment — internal network (IIS)

This guide prepares LeaversForm for **live use on the corporate network only**. The app is not intended to be exposed to the public internet.

For day-to-day support (statuses, in-app ops, troubleshooting), see **[LeaversForm IT Operations Guide](LeaversForm-IT-Guide.html)**.

## Architecture overview

```text
[Staff browser on LAN/VPN]
        |
        v
[Internal IIS server — HTTPS]
  LeaversForm.Web
    - Razor Pages UI
    - Background checker (every 5 min — due leavers + urgent)
    - Reconciliation (every ~30 sec — stuck Processing)
    - SQL Server (local)
        |
        +-- outbound 443 --> login.microsoftonline.com (SSO + tokens)
        +-- outbound 443 --> graph.microsoft.com (user search + email)
        +-- local pwsh --> LeaverProcess.ps1 (on-prem AD + Graph/Exchange)
```

| Traffic | Direction | Required? |
|---------|-----------|-----------|
| Users → internal site URL | Inbound to IIS (LAN/VPN only) | Yes |
| Server → Microsoft SSO/Graph | Outbound HTTPS 443 | Yes |
| Server → SQL Server | Internal | Yes |
| Web app → PowerShell / AD | Internal | Yes |
| Public internet → IIS | Inbound | **No** |

> **Do not** schedule `LeaversForm.Runner` alongside the web app — both would process the same queue. Runner is a CLI fallback only (`--reconcile`).

## Before you start

- Windows Server with IIS and **ASP.NET Core 8 Hosting Bundle** installed
- SQL Server Express (or full) on the same server or reachable internally
- Entra ID app registration (see below)
- Internal DNS name, e.g. `leavers.internal.yoursclothing.co.uk`
- Internal TLS certificate for that hostname
- Service accounts for IIS app pool, Runner task, and PowerShell (least privilege)

## 1. Entra ID app registration

Create an app registration in the Microsoft Entra admin centre:

**Authentication (web redirect URIs)** — use your internal URL:

- `https://leavers.internal.yoursclothing.co.uk/signin-oidc`
- `https://leavers.internal.yoursclothing.co.uk/signout-callback-oidc`

**API permissions**

| Permission | Type | Purpose |
|------------|------|---------|
| `User.Read` | Delegated | Sign-in |
| `User.Read.All` | Application | Leaver search |
| `Mail.Send` | Application | Notification emails |

Grant **admin consent** for application permissions.

Create a **client secret** and store it securely (not in git).

**Sign-in audience:** single tenant (your organisation only).

## 2. Network and firewall

### Users

- Publish the site on an **internal DNS name** only.
- Restrict IIS bindings to the internal network (no public IP / no WAF exposure unless via VPN).
- During login, the **user’s browser** redirects to Microsoft (`login.microsoftonline.com`). Corporate networks usually allow this already.

### App server outbound allow-list (HTTPS 443)

Allow the IIS server to reach:

| Host | Purpose |
|------|---------|
| `login.microsoftonline.com` | SSO and token acquisition |
| `graph.microsoft.com` | User search and send mail |
| `login.microsoft.com` | Some Entra redirect flows |

Optional (if your tenant uses them): `*.msauth.net`, `login.windows.net`.

Run on the server (as Administrator) when ready:

```powershell
.\deploy\configure-firewall-outbound.ps1
```

Or configure the same rules on your corporate firewall/proxy.

**Do not** give the server unrestricted internet access unless policy requires it — allow-list is preferred.

## 3. Publish the application

From a build machine with the .NET 8 SDK:

```powershell
.\deploy\publish-production.ps1 -OutputPath "C:\Deploy\LeaversForm"
```

Copy the output to the IIS server, e.g. `C:\inetpub\LeaversForm`.

## 4. Configure secrets (never commit these)

Set production secrets via **environment variables** on the IIS app pool or **IIS Configuration Editor** → `system.webServer/aspNetCore` → `environmentVariables`:

| Variable | Example |
|----------|---------|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `AzureAd__TenantId` | your tenant GUID |
| `AzureAd__ClientId` | app registration client ID |
| `AzureAd__ClientSecret` | client secret value |
| `ConnectionStrings__DefaultConnection` | SQL connection string (if overriding JSON) |

Use the same `AzureAd__*` variables if you run `LeaversForm.Runner` manually as a fallback (e.g. `--reconcile`).

Update placeholders in:

- `LeaversForm.Web\appsettings.Production.json` — `BaseUrl`, `AllowedHosts`, SQL server name, paths
- `LeaversForm.Runner\appsettings.Production.json` — SQL server name, paths

## 5. Database

On the server (or from a management machine):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet ef database update --project LeaversForm.Data --startup-project LeaversForm.Web
```

Or run migrations once on first deploy using the published web folder as startup project.

The app seeds authorising managers, security exclusions, and super users on first start.

## 6. IIS setup

Run as Administrator (adjust paths and hostname):

```powershell
.\deploy\configure-iis.ps1 `
  -SiteName "LeaversForm" `
  -HostName "leavers.internal.yoursclothing.co.uk" `
  -PhysicalPath "C:\inetpub\LeaversForm\Web" `
  -AppPoolIdentity "YOURDOMAIN\svc-leaversform"
```

Manual checklist:

- [ ] App pool: **No managed code**, identity = dedicated service account
- [ ] HTTPS binding with internal certificate
- [ ] Folder permissions: app pool read on site; write on `LogFolderPath`
- [ ] `ASPNETCORE_ENVIRONMENT=Production`
- [ ] Confirm site loads only on internal network

## 7. Automated leaver processing

The Web application includes a hosted background service. It checks every **5 minutes**
for requests due at 5pm UK time and runs `LeaverProcess.ps1`. Admin **urgent switch-off**
requests (submitted with “Switch off their account today”) are picked up immediately by the
same service. A separate reconciliation loop runs every **~30 seconds** to fix stuck
Processing requests (read logs, terminate hung processes, mark Failed or Completed).

For this to run continuously under IIS:

- App pool **Start Mode** = `AlwaysRunning`
- App pool **Idle Time-out** = `0`
- Site **Preload Enabled** = `True`
- App pool identity must have permission to run PowerShell, access AD, read the certificate private key, and write `LogFolderPath`
- Install PowerShell 7 (`pwsh.exe`) and all modules required by `LeaverProcess.ps1`
- Copy the script to the configured `PowerShellScriptPath`
- Set `LeaversForm:PowerShellTenantId`, `PowerShellAppId`,
  `PowerShellCertificateThumbprint`, `PowerShellExchangeOrganization`, and
  `PowerShellDomainController` in the production configuration
- Ensure the IIS app-pool identity can read the certificate private key in
  `Cert:\LocalMachine\My`

The standalone `LeaversForm.Runner` remains available as an operational fallback, but
do not run it at the same time as the Web hosted service or a request could be picked
up twice.

## 8. Go-live checklist

- [ ] `EmailTestMode` and `ScriptTestMode` are **false** in Production config (or Processing settings shows **LIVE**)
- [ ] `LeaversForm:BaseUrl` matches the internal HTTPS URL
- [ ] Entra redirect URIs match exactly
- [ ] Super users configured on **Admin → User roles** (or verify seed accounts)
- [ ] PowerShell 7 and required modules installed
- [ ] PowerShell script path and certificate tested manually with `-WhatIf`
- [ ] Firewall outbound rules tested (`Test-NetConnection login.microsoftonline.com -Port 443`)
- [ ] Submit a test leaver request end-to-end
- [ ] Confirm the Web background service picks up a due request (or admin urgent switch-off) in test window
- [ ] Super-user can open **Processing settings** and **Diagnostics** on a test request

## 9. Rollback / test modes

To validate without sending mail or running scripts:

```json
"Graph": { "EmailTestMode": true },
"LeaversForm": { "ScriptTestMode": true }
```

Or super-user: **Admin → Processing settings → Switch to TEST** (immediate, no restart).

Revert to LIVE before real leavers are processed.

### In-app operations (super-user)

| UI location | Action |
|-------------|--------|
| Admin → Processing settings | LIVE/TEST toggle, Fix stuck requests, Fix automation setup |
| Request Details → Diagnostics | Audit timeline, platform status, log download, fix one stuck request |
| Admin → Blocked accounts | Security exclusions |
| Admin → User roles | Assign admin / super-user |

Automatic reconciliation runs every ~30 seconds while the web app is running. Cancel on a Processing request attempts to stop the PowerShell process.

## Related files

| Path | Purpose |
|------|---------|
| `deploy/publish-production.ps1` | Build Release publish bundle |
| `deploy/configure-iis.ps1` | IIS site and app pool |
| `deploy/configure-firewall-outbound.ps1` | Windows Firewall outbound allow-list |
| `docs/LeaversForm-IT-Guide.html` | **Full IT handbook** — architecture, statuses, in-app ops, troubleshooting (print/PDF) |
| `LeaversForm.Web/appsettings.Production.json` | Production app settings template |
| `LeaversForm.Runner/appsettings.Production.json` | Runner production settings (fallback CLI only) |
