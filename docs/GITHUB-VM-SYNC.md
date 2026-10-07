# GitHub repo and automatic VM deploy

Push to **`main`** from your PC; a **self-hosted runner on the VM** builds and deploys.

| VM OS | Runner labels | Repo variable | Workflow |
|-------|---------------|---------------|----------|
| **Linux** (current) | `self-hosted`, `Linux`, `leavers-vm-linux` | `LINUX_APP_PATH` e.g. `/opt/leaversform/app` | `deploy-linux-vm.yml` |
| **Windows IIS** (legacy) | `self-hosted`, `windows`, `leavers-vm` | `IIS_WEB_PATH`, optional `IIS_RUNNER_PATH` | `deploy-windows-vm.yml` |

Use the **Linux** row for your current VM. Windows IIS setup is frozen on branch **`legacy/windows-iis-working`** — see [BACKUP-WINDOWS-IIS.md](BACKUP-WINDOWS-IIS.md).

Full Linux install: **[LINUX-DEPLOYMENT.md](LINUX-DEPLOYMENT.md)**.

## Linux runner setup (summary)

1. GitHub → **Settings → Actions → Runners → New self-hosted runner → Linux**.
2. Install on the VM; add label **`leavers-vm-linux`**.
3. **Settings → Actions → Variables** → `LINUX_APP_PATH` = `/opt/leaversform/app`.
4. Complete one-time install per [LINUX-DEPLOYMENT.md](LINUX-DEPLOYMENT.md) (nginx, systemd, secrets).

## Day-to-day

```powershell
git add -A
git commit -m "Your change"
git push origin main
```

Check **Actions** → **Deploy to Linux VM**.

## Manual pull (no Actions)

```bash
git pull origin main
./deploy/linux/publish-local.sh /opt/leaversform/app
sudo systemctl restart leaversform
```

## Security

- `.gitignore` excludes `appsettings.*.local.json`, `*.pfx`, and `*.db`.
- Never commit Entra secrets or certificates.
