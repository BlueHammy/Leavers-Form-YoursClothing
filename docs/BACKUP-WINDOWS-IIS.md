# Windows IIS backup (known-good)

Before the Linux migration, the working **Windows + IIS** deployment was frozen here:

| Backup | Location |
|--------|----------|
| **Branch** | [`legacy/windows-iis-working`](https://github.com/BlueHammy/Leavers-Form-YoursClothing/tree/legacy/windows-iis-working) |
| **Tag** | [`windows-iis-v1`](https://github.com/BlueHammy/Leavers-Form-YoursClothing/releases/tag/windows-iis-v1) |

## Restore on a Windows server

```powershell
git fetch origin
git checkout legacy/windows-iis-working
# or: git checkout windows-iis-v1
```

Then follow [PRODUCTION-DEPLOYMENT.md](PRODUCTION-DEPLOYMENT.md) and use the Windows GitHub Actions workflow (`.github/workflows/deploy-windows-vm.yml`) with IIS paths.

`main` continues on **Linux**; use this branch/tag whenever you need the previous IIS setup unchanged.
