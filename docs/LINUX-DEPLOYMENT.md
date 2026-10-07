# Linux production deployment

LeaversForm runs on **ASP.NET Core 8** under **systemd + nginx**. Offboarding still invokes **`LeaverProcess.ps1`** via **PowerShell 7 (`pwsh`)** — Entra/Exchange steps can run on Linux; **on-prem AD** steps require network reachability to your DCs or a hybrid worker (see [BACKUP-WINDOWS-IIS.md](BACKUP-WINDOWS-IIS.md)).

For the frozen Windows/IIS version, use branch **`legacy/windows-iis-working`** or tag **`windows-iis-v1`**.

## Quick start

[`Final-Build.sh`](../Final-Build.sh) is the Linux equivalent of `Final-Build.ps1`. On Ubuntu it installs the .NET 8 SDK, nginx, and PowerShell 7, publishes the site, and starts it under systemd.

```bash
sudo ./Final-Build.sh
```

Open `http://<this-machine>/`. That default is a **test host**: SQLite, script and email test mode on, and development sign-in when the Entra tenant is empty. Leaver accounts are not disabled.

Live hosting uses the same script with `--live` and the same Entra, SQL, and domain-controller values as `Final-Build.ps1`. Run `sudo ./Final-Build.sh --help` for the flags. The script writes `/opt/leaversform/FINAL-SETUP-CHECKLIST.txt` when it finishes.

## 1. VM prerequisites

- Ubuntu 22.04+ (or similar), .NET 8 runtime + ASP.NET Core runtime
- **nginx**, **PowerShell 7** (`pwsh`)
- Outbound HTTPS to `login.microsoftonline.com`, `graph.microsoft.com`, Exchange Online
- Clone: `git clone https://github.com/BlueHammy/Leavers-Form-YoursClothing.git`

```bash
# Ubuntu 22.04 — installs .NET 8 SDK (required for publish), runtime, nginx, pwsh
sudo ./deploy/linux/install-prerequisites.sh
```

**`dotnet: command not found`** — run the install script (needs **SDK** for publish). If apt fails (non-Ubuntu VM), the script falls back to Microsoft’s install script into `/usr/share/dotnet` and `/usr/local/bin/dotnet`:

```bash
git pull origin main
sudo ./deploy/linux/install-prerequisites.sh
source /etc/profile.d/dotnet.sh
dotnet --list-sdks    # must show 8.x
sudo ./deploy/linux/publish-local.sh /opt/leaversform/app
```

Still stuck? Run `./deploy/linux/diagnose-environment.sh` and check the script output.

**Git pull blocked** on `publish-local.sh`: discard VM edits — `git checkout -- deploy/linux/publish-local.sh` then `git pull`.

## 2. Layout

| Path | Purpose |
|------|---------|
| `/opt/leaversform/app` | Published web app (git pull / CI deploy target) |
| `/opt/leaversform/scripts` | `LeaverProcess.ps1` (copied from publish output) |
| `/var/log/leaversform` | Offboarding logs |
| `/etc/leaversform/secrets` | **Not in git** — PFX, `appsettings.Production.local.json` |

## 3. Secrets (never commit)

Copy examples and edit on the server only:

```bash
sudo mkdir -p /etc/leaversform/secrets
sudo cp deploy/linux/appsettings.Production.local.json.example /etc/leaversform/secrets/appsettings.Production.local.json
sudo nano /etc/leaversform/secrets/appsettings.Production.local.json
# Place automation.pfx in /etc/leaversform/secrets/ and chmod 600
```

Environment for systemd (see `deploy/linux/leaversform.service`):

- `ASPNETCORE_ENVIRONMENT=Production`
- `LeaversForm__PowerShellCertificatePassword` — PFX password
- `AzureAd__*` — as in Windows production

Symlink or copy secrets into `/opt/leaversform/app/appsettings.Production.local.json` after deploy.

## 4. First install

```bash
cd Leavers-Form-YoursClothing
./deploy/linux/publish-local.sh /opt/leaversform/app
sudo cp deploy/linux/leaversform.service /etc/systemd/system/
sudo cp deploy/linux/nginx-leaversform.conf /etc/nginx/sites-available/leaversform
sudo ln -sf /etc/nginx/sites-available/leaversform /etc/nginx/sites-enabled/
sudo systemctl daemon-reload
sudo systemctl enable --now leaversform
sudo nginx -t && sudo systemctl reload nginx
```

## 5. Auto-deploy from GitHub (push to `main`)

1. **Self-hosted Linux runner** on the VM with labels: `self-hosted`, `Linux`, `leavers-vm-linux`
2. Repository **Variables**:
   - `LINUX_APP_PATH` = `/opt/leaversform/app`

Workflow: `.github/workflows/deploy-linux-vm.yml`

## 6. Go-live checklist

- [ ] Entra app registration + redirect URI for Linux URL
- [ ] `Graph:EmailTestMode` false, redirect cleared when ready
- [ ] Processing **LIVE** only after testing (`ScriptTestMode` false in local settings)
- [ ] PFX thumbprint matches `LeaversForm:PowerShellCertificateThumbprint`
- [ ] Confirm AD policy: Entra-only disable vs LDAP to DC

## 7. Hybrid fallback

If AD/EXO automation is unreliable on Linux, keep a **Windows worker** (branch `legacy/windows-iis-working`) and queue jobs — contact IT before changing production leaver flow.
