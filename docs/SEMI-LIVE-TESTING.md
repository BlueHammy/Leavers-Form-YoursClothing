# Semi-live testing with real Entra SSO

Use this guide to test LeaversForm locally against your **real Microsoft Entra ID (Azure AD) tenant** and **real directory users**, while keeping email and PowerShell in test mode.

## How auth modes work

| Configuration | Behaviour |
|---------------|-----------|
| `Development` + empty `AzureAd:TenantId` | Dev auth bypass — auto signed in as Michael Newton |
| `Development` or `SemiLive` + `AzureAd:TenantId` set | Real Microsoft SSO via OpenID Connect |
| `Production` | Real SSO (see [PRODUCTION-DEPLOYMENT.md](PRODUCTION-DEPLOYMENT.md)) |

Setting `AzureAd:TenantId` (via user secrets or a local config file) is the switch that turns on real SSO. Dev bypass is disabled automatically.

## What you need before starting

- .NET 8 SDK
- Access to **Microsoft Entra admin centre** for `yoursclothing.co.uk` (to create or edit an app registration)
- Permission to grant admin consent for Graph application permissions (or ask your Entra admin)
- Outbound HTTPS to `login.microsoftonline.com` and `graph.microsoft.com` from your machine

## Step 1 — Entra app registration

In [Microsoft Entra admin centre](https://entra.microsoft.com) → **App registrations** → **New registration**:

| Field | Value |
|-------|-------|
| Name | `LeaversForm (Local Dev)` or similar |
| Supported account types | **Accounts in this organizational directory only** (single tenant) |
| Redirect URI | **Web** → see below |

### Redirect URIs (localhost)

Add **all four** under **Authentication → Platform configurations → Web** (not "Single-page application"):

| URI | Used when |
|-----|-----------|
| `https://localhost:7219/signin-oidc` | HTTPS launch profile |
| `https://localhost:7219/signout-callback-oidc` | Sign-out callback (HTTPS) |
| `http://localhost:5044/signin-oidc` | HTTP-only profile |
| `http://localhost:5044/signout-callback-oidc` | Sign-out callback (HTTP) |

> **Important:** Redirect URIs must be on the **Web** platform. If they were added under **Single-page application**, remove them and re-add under **Web**.

Also enable under **Implicit grant and hybrid flows**:

- **ID tokens** — required; fixes `AADSTS700054` if sign-in fails
- **Access tokens** — leave unchecked
- **Allow public client flows** — No

Under **Advanced settings** (or **Logout URL** / **Front-channel logout URL**), set:

| Field | Value |
|-------|-------|
| Front-channel logout URL | `http://localhost:5044/signout-callback-oidc` |

> **Note:** `/signout-callback-oidc` is handled by the app middleware — it is not a page you browse to directly. Opening it in a browser shows a blank page, which is normal. After sign-out, you should land on `/Account/SignedOut`.

Copy these values from the app registration **Overview** page:

- **Application (client) ID** → `AzureAd:ClientId`
- **Directory (tenant) ID** → `AzureAd:TenantId`

### Client secret

**Certificates & secrets** → **New client secret** → copy the **Value** immediately (not the Secret ID) → `AzureAd:ClientSecret`.

### API permissions

Under **API permissions**, add:

| Permission | Type | Purpose |
|------------|------|---------|
| `User.Read` | Delegated | Sign-in |
| `User.Read.All` | Application | Leaver search in Create form |
| `Mail.Send` | Application | Notification emails (kept in test mode locally) |

Click **Grant admin consent for [your org]**.

> **Note:** With `Graph:EmailTestMode` and `LeaversForm:EmailTestMode` left as `true` (default for semi-live), emails are logged but not sent. Graph permissions are still required if you later disable test mode.

## Step 2 — Configure secrets (do not commit)

Choose **one** of these methods.

### Option A — User secrets (recommended)

From the repo root:

```powershell
dotnet user-secrets init --project LeaversForm.Web
dotnet user-secrets set "AzureAd:TenantId" "<your-tenant-id>" --project LeaversForm.Web
dotnet user-secrets set "AzureAd:ClientId" "<your-client-id>" --project LeaversForm.Web
dotnet user-secrets set "AzureAd:ClientSecret" "<your-client-secret>" --project LeaversForm.Web
```

User secrets work in both `Development` and `SemiLive` environments.

### Option B — Local JSON file (gitignored)

```powershell
Copy-Item LeaversForm.Web\appsettings.SemiLive.local.json.example LeaversForm.Web\appsettings.SemiLive.local.json
```

Edit `appsettings.SemiLive.local.json` with your tenant ID, client ID, and client secret. This file is gitignored via `appsettings.*.local.json`.

### Option C — Environment variables

```powershell
$env:AzureAd__TenantId = "<your-tenant-id>"
$env:AzureAd__ClientId = "<your-client-id>"
$env:AzureAd__ClientSecret = "<your-client-secret>"
```

## Step 3 — Reset leaver request data (optional)

Before a clean SSO test run, clear all submitted requests:

```powershell
.\scripts\reset-leaver-requests.ps1
```

This removes all rows from `LeaverRequests`, related `AuditLogs`, and `LeaverRequestAuthorisingManagers`. It **does not** remove authorising managers, exclusions, or seeded super users (Michael Newton, David Lane, Vlad Kucherov).

## Step 4 — Run the app

### SemiLive profile (recommended for SSO testing)

In Visual Studio / Rider / VS Code, select launch profile **SemiLive (Entra SSO)**, or:

```powershell
dotnet run --project LeaversForm.Web --launch-profile "SemiLive (Entra SSO)"
```

This loads `appsettings.SemiLive.json` (test modes on, correct localhost URLs) plus your secrets.

### Development profile with SSO

You can also stay on `Development` — if `AzureAd:TenantId` is set via user secrets, real SSO is used instead of the dev bypass:

```powershell
dotnet run --project LeaversForm.Web
```

### Return to dev bypass

Remove or clear the Azure AD secrets:

```powershell
dotnet user-secrets remove "AzureAd:TenantId" --project LeaversForm.Web
dotnet user-secrets remove "AzureAd:ClientId" --project LeaversForm.Web
dotnet user-secrets remove "AzureAd:ClientSecret" --project LeaversForm.Web
```

Or delete `appsettings.SemiLive.local.json`.

## Step 5 — Verify SSO is working

1. Open `https://localhost:7219` or `http://localhost:5044` (accept the dev certificate warning for HTTPS if prompted).
2. You should be redirected to **Microsoft sign-in** — not auto-logged in as Michael Newton.
3. Sign in with a real `@yoursclothing.co.uk` account.
4. After redirect back, confirm the header shows **your** name/email, not `michaelnewton@yoursclothing.co.uk`.
5. On **Create**, search for a real colleague — results should come from Graph (not the three fake dev users: Alex Johnson, Sam Taylor, Jordan Lee).
6. Visit `/Account/Logout` — you should sign out of the app (and optionally Microsoft, depending on tenant policy).

### Role testing

App roles come from the `AppUsers` table (seeded super users):

| Email | Role |
|-------|------|
| `michaelnewton@yoursclothing.co.uk` | Super user |
| `davidlane@yoursclothing.co.uk` | Super user |
| `vladkucherov@yoursclothing.co.uk` | Super user |

Everyone else gets the **User** role until added via **Admin → User roles** (super-users only). Sign in as Michael, David, or Vlad to test admin/super-user features.

**Super-user UI to try:** Admin → **Processing settings** (LIVE/TEST toggle, fix stuck requests), **Blocked accounts**, and per-request **Diagnostics** on Details.

### Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| `AADSTS700054` — id_token not enabled | Enable **ID tokens** under Authentication → Implicit grant; ensure redirect URIs are **Web** platform, not SPA |
| Still auto-signed in as Michael Newton | `AzureAd:TenantId` is empty — check user secrets / local JSON |
| `AADSTS50011` redirect URI mismatch | Add the exact `https://localhost:7219/signin-oidc` (or `http://localhost:5044/signin-oidc`) URI in Entra. For LAN testers, also add `http://YOUR-PC-NAME:5044/signin-oidc` |
| User search returns fake dev names | TenantId not loaded — Graph falls back to dev users |
| User search fails / empty | Missing `User.Read.All` application permission or admin consent |
| Access denied to admin pages | Your account is not in `AppUsers` as Admin/SuperUser — ask a super-user to add you on Admin → User roles |

## Safety defaults in semi-live

`appsettings.SemiLive.json` keeps script execution disabled, but enables controlled
email testing:

- `Graph:EmailTestMode: false` — real emails are sent
- `LeaversForm:EmailTestMode: false` — real emails are sent
- `Graph:ItCcAddress: michaelnewton@yoursclothing.co.uk` — testing CC
- `Graph:ErrorAlertMailbox: michaelnewton@yoursclothing.co.uk` — testing alert recipient
- `Graph:RedirectAllEmailsTo: michaelnewton@yoursclothing.co.uk` — redirects every email to Michael
- `LeaversForm:ScriptTestMode: true` — PowerShell script not executed
- SQLite database `leaversform.db` — no production SQL Server

The web app checks for due requests every five minutes, but `ScriptTestMode` means
`LeaverProcess.ps1` is not launched during semi-live testing. It only logs what it
would process. Leave this enabled until the script, certificate, PowerShell modules,
and service-account permissions have been tested safely with `-WhatIf`.

**In-app toggle:** Super-users can also switch LIVE/TEST from **Admin → Processing settings** without restarting the app (persists to local JSON). Admins see a red banner when test mode is on.

**Urgent switch-off:** Only admins/super-users see “Switch off their account today” on the submit form. Regular users cannot trigger immediate account disable.

Redirected test emails include the original intended recipient in the subject and
an amber notice at the top of the message. Production changes the CC and alert
recipient to `it@yoursclothing.co.uk` and clears the redirect.

## LAN testing (colleagues on your network)

Microsoft Entra **only accepts redirect URIs that start with `https://` or `http://localhost`**.  
Plain `http://10.x.x.x:5044` or `http://YOUR-PC-NAME:5044` is rejected in the portal — that is expected.

To let colleagues test on your network, use HTTPS with the helper script:

1. Run from the repo root (Administrator recommended once, for the firewall rule):

```powershell
.\Start-LanTest.ps1
```

2. The script creates a self-signed certificate, opens port **5044**, and starts the app on **HTTPS**.

3. Share the printed URL, for example:
   - `https://Laptop-4060342:5044`
   - `https://10.222.0.83:5044`

4. In **Entra → App registrations → Authentication → Web**, add **HTTPS** redirect URIs:

| URI | Purpose |
|-----|---------|
| `https://YOUR-PC-NAME:5044/signin-oidc` | Sign-in callback for LAN testers |
| `https://YOUR-PC-NAME:5044/signout-callback-oidc` | Sign-out callback |

If testers may use your IP address instead of the PC name, add the same paths with `https://YOUR-IP:5044/...`.

5. Testers accept the browser certificate warning (self-signed cert), then sign in with their `@yoursclothing.co.uk` account.

They must be on the same network (or VPN) and able to reach your machine.

## Quick reference

```powershell
# One-time Entra setup → user secrets (see Step 2)

# Reset requests before testing
.\scripts\reset-leaver-requests.ps1

# Run with real SSO
dotnet run --project LeaversForm.Web --launch-profile "SemiLive (Entra SSO)"

# Let colleagues test on your network (port 5044)
.\Start-LanTest.ps1

# Verify secrets are set (values are hidden)
dotnet user-secrets list --project LeaversForm.Web
```
