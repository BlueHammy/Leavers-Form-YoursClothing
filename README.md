# LeaversForm

LeaversForm is an ASP.NET Core 8 Razor Pages application for submitting, reviewing and safely processing leaver requests. It includes SQLite development storage, SQL Server production support, Microsoft Graph user search/email, audit logging, Excel export, in-app IT operations, and a PowerShell offboarding script (`LeaverProcess.ps1`).

## Documentation

| Document | Audience | Contents |
|----------|----------|----------|
| **[IT Operations Guide (HTML)](docs/LeaversForm-IT-Guide.html)** | IT / support | Architecture, statuses, Processing settings, diagnostics, troubleshooting, go-live |
| **[Production deployment](docs/PRODUCTION-DEPLOYMENT.md)** | Server install | IIS, Entra, firewall, secrets, checklist |
| **[GitHub → VM sync](docs/GITHUB-VM-SYNC.md)** | Developers / IT | Clone, self-hosted runner, auto-deploy on push |
| **[Semi-live testing](docs/SEMI-LIVE-TESTING.md)** | Developers | Real Entra SSO on localhost / LAN |

Open the IT guide in a browser and print to PDF if you need a printable handbook.

## What staff see

- **Requests** list with plain statuses: Waiting, Processing, Done, Needs IT help
- **Submit** form — search colleague, leaving date, equipment, authorising managers
- Urgent same-day switch-off is **admin/super-user only** on the submit form

## What IT super-users get in the app

- **Admin → Processing settings** — LIVE/TEST toggle (no restart), fix stuck requests, repair modules
- **Diagnostics** on each request — audit timeline, platform status, log downloads
- **Blocked accounts** and **User roles**

## Prerequisites

- .NET 8 SDK
- SQL Server Express for production (SQLite is used in development)
- An Entra ID app registration with delegated sign-in and application Graph permissions `User.Read.All` and `Mail.Send`, with admin consent
- IIS and a service account if hosting on Windows
- Separate Entra **automation** app with certificate for `LeaverProcess.ps1` (see IT guide)

## Local setup

```powershell
dotnet restore
dotnet run --project LeaversForm.Web
```

The development configuration uses `leaversform.db` and email/script test modes. With an empty `AzureAd:TenantId`, the app auto-signs in as Michael Newton (dev bypass). For **real Entra SSO on localhost**, see **[Semi-live testing guide](docs/SEMI-LIVE-TESTING.md)**.

LAN testing:

```powershell
.\Start-LanTest.ps1
```

Quick start with user secrets:

```powershell
dotnet user-secrets --project LeaversForm.Web init
dotnet user-secrets --project LeaversForm.Web set AzureAd:TenantId "<tenant-id>"
dotnet user-secrets --project LeaversForm.Web set AzureAd:ClientId "<client-id>"
dotnet user-secrets --project LeaversForm.Web set AzureAd:ClientSecret "<secret>"
dotnet run --project LeaversForm.Web --launch-profile "SemiLive (Entra SSO)"
```

Set `Graph:SenderMailbox`, `Graph:ErrorAlertMailbox`, and `LeaversForm:BaseUrl` before enabling live Graph email.

## Live processing (dev laptop)

```powershell
.\Initialize-LiveProcessingTest.ps1   # first-time cert + paths
.\Enable-LiveProcessing.ps1           # or Set-LiveProcessing.ps1
# Or use Admin → Processing settings → Switch to LIVE in the running app
```

Safe testing: `.\Set-TestProcessing.ps1` or Processing settings → TEST.

## Database and deployment

The application creates the development database and seeds authorising managers on first run. For production:

```powershell
dotnet ef database update --project LeaversForm.Data --startup-project LeaversForm.Web
dotnet publish LeaversForm.Web -c Release
```

See **[Production deployment guide](docs/PRODUCTION-DEPLOYMENT.md)** and **`Final-Build.ps1`** for IIS setup.

## Processing model

The **web app** includes a background service that:

- Checks every **5 minutes** for requests due at 5:00 PM UK on the leaving date
- Processes **urgent** (admin) requests immediately
- **Reconciles** stuck Processing requests every ~30 seconds

Do **not** run `LeaversForm.Runner` on a schedule alongside the web app in production (duplicate processing). Runner remains a CLI fallback (`--reconcile`).

## Testing

```powershell
dotnet test
```

Use `EmailTestMode` and `ScriptTestMode` (or the in-app LIVE/TEST toggle) while validating forms, Graph configuration and the PowerShell integration. The application keeps an append-only audit record for create, edit, cancel, retry, script, mode changes, and error actions.

## Production (internal network)

For live deployment on **IIS inside the corporate network** (no public internet exposure):

- **`Final-Build.ps1`** — elevated server bootstrap
- **[Production deployment guide](docs/PRODUCTION-DEPLOYMENT.md)**
- **[IT Operations Guide](docs/LeaversForm-IT-Guide.html)** — day-to-day support
- **`deploy/publish-production.ps1`**, **`deploy/configure-iis.ps1`**, **`deploy/configure-firewall-outbound.ps1`**
