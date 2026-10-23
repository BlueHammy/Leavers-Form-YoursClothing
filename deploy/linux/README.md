# Linux VM deploy from git

After cloning or pulling this branch on **sh-pp-01** (or any Ubuntu host):

```bash
cd ~/Source/Leavers-Form-YoursClothing
git fetch origin
git checkout bluehammy/linux-final-build-fb33
git pull origin bluehammy/linux-final-build-fb33
sudo bash deploy/linux/setup-from-git.sh --skip-tests --bind-lan
```

That publishes to `/opt/leaversform/app`, copies PowerShell scripts to `/opt/leaversform/scripts`, installs `leaversform.service`, configures nginx, and creates `/etc/leaversform/secrets/appsettings.Development.local.json` **only if it does not exist**.

## Secrets (never in git)

| Path | Purpose |
|------|---------|
| `/etc/leaversform/secrets/appsettings.Development.local.json` | Entra + automation settings |
| `/etc/leaversform/secrets/automation.pfx` or `automation-new.pfx` | PowerShell automation private key |
| `/etc/systemd/system/leaversform.service.d/override.conf` | PFX password (`LeaversForm__PowerShellCertificatePassword`) |

Copy the example and edit:

```bash
sudo cp deploy/linux/appsettings.Development.local.json.example /etc/leaversform/secrets/appsettings.Development.local.json
sudo nano /etc/leaversform/secrets/appsettings.Development.local.json
sudo cp /etc/leaversform/secrets/appsettings.Development.local.json /opt/leaversform/app/
sudo systemctl restart leaversform
```

Full Entra / Exchange / AD steps: [docs/LINUX-ENTRA-SETUP.md](../../docs/LINUX-ENTRA-SETUP.md).

## Individual scripts

| Script | Role |
|--------|------|
| `setup-from-git.sh` | One-shot publish + systemd + nginx |
| `final-build.sh` (repo root) | Full init including optional `--live` |
| `sync-scripts.sh` | `LeaverProcess.ps1` + bootstrap → `/opt/leaversform/scripts` |
| `install-systemd.sh` | Write `leaversform.service` with detected `dotnet` |
| `install-app-settings.sh` | Seed secrets JSON from example |
| `import-automation-pfx.sh` | Optional: import PFX into `Cert:\LocalMachine\My` |
| `test-leaver-whatif.sh` | `-WhatIf` offboarding using settings JSON |
| `diagnose-environment.sh` | Read-only environment dump |

## Verify

```bash
curl -fsSL http://127.0.0.1:5044/ | grep -E 'Leaver requests|Development auth bypass'
sudo journalctl -u leaversform -n 30 --no-pager | grep ScriptTestMode
```

## HTTP 500 / `MissingMethodException` on `/Leavers/List`

Usually **stale DLLs** in `/opt/leaversform/app` from an older publish. Re-publish (the script clears the folder but keeps local settings and `leaversform.db`):

```bash
cd ~/Source/Leavers-Form-YoursClothing
sudo bash deploy/linux/publish-local.sh /opt/leaversform/app
sudo cp /etc/leaversform/secrets/appsettings.Development.local.json /opt/leaversform/app/ 2>/dev/null || true
sudo systemctl restart leaversform
```

## HTTPS on port 5044 (Entra test on sh-pp-01)

Kestrel stays on `http://127.0.0.1:5044`. nginx listens on **`5044` with TLS**:

```bash
sudo bash deploy/linux/install-test-tls-5044.sh
# or: sudo bash deploy/linux/install-test-tls-5044.sh --host sh-pp-01
```

Then set `LeaversForm:BaseUrl` to `https://sh-pp-01:5044`, add matching redirect URIs in Entra, restart the service, and browse **https** (not http).

## `git pull` blocked on deploy scripts

If pull aborts because `deploy/linux/*.sh` was edited on the VM, keep the branch versions (recommended):

```bash
git checkout -- deploy/linux/diagnose-environment.sh deploy/linux/install-prerequisites.sh deploy/linux/publish-local.sh
git pull origin bluehammy/linux-final-build-fb33
```

To keep a copy of your VM edits first:

```bash
git stash push -m "vm deploy scripts" -- deploy/linux/
git pull origin bluehammy/linux-final-build-fb33
```
