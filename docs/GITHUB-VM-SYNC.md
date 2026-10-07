# GitHub repo and automatic VM deploy

This project can live on GitHub so you edit on your laptop and **push to `main`**; a **self-hosted runner on the VM** builds and syncs to IIS.

## 1. Clone on the VM (first time)

```powershell
cd C:\Source   # or your preferred folder
git clone https://github.com/<your-org-or-user>/Leavers-Form-YoursClothing.git
cd Leavers-Form-YoursClothing
```

Copy secrets locally on the VM only (never commit):

- `LeaversForm.Web\appsettings.Production.local.json` (from `deploy\appsettings.Production.local.json.example`)
- Certificate and IIS setup per [PRODUCTION-DEPLOYMENT.md](PRODUCTION-DEPLOYMENT.md)

## 2. Self-hosted runner on the VM

1. On GitHub: **Repo → Settings → Actions → Runners → New self-hosted runner → Windows**.
2. Follow GitHub’s install commands on the VM (download, configure, run as a service).
3. When configuring, add an extra label: **`leavers-vm`** (the deploy workflow expects `self-hosted`, `windows`, `leavers-vm`).

## 3. Repository variables (GitHub)

**Settings → Secrets and variables → Actions → Variables**

| Variable | Example |
|----------|---------|
| `IIS_WEB_PATH` | `C:\inetpub\LeaversForm\Web` |
| `IIS_RUNNER_PATH` | `C:\inetpub\LeaversForm\Runner` (optional) |

The workflow **does not** overwrite `appsettings.Production.local.json` on the server.

## 4. Day-to-day workflow

On your dev machine:

```powershell
git add -A
git commit -m "Describe your change"
git push origin main
```

GitHub Actions runs on the VM, publishes Release, robocopy-syncs to IIS, and recycles the **LeaversForm** app pool.

## 5. Manual pull (without Actions)

If you only want git sync and will publish yourself:

```powershell
git pull origin main
.\deploy\publish-production.ps1 -OutputPath C:\Deploy\LeaversForm
# then copy outputs and recycle IIS per PRODUCTION-DEPLOYMENT.md
```

## Security

- `.gitignore` excludes `appsettings.*.local.json` and `*.db`.
- Do not commit Entra client secrets, certificates, or PFX files.
