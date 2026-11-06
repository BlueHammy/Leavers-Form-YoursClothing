# Connect the Linux site to Entra, Exchange, and AD

Use this after the site is running on the VM (`systemctl is-active leaversform` prints `active`).

The VM in these steps is **sh-pp-01**. The app is in **Development**, on **http://127.0.0.1:5044**, with **script test mode on**. That means sign-in is still the Michael Newton bypass, and leaver accounts are not disabled.

Do the steps in order. Stop at the end of each part and check the result before continuing.

There are **two** Entra app registrations. Do not reuse one for both jobs.

| App | What it does | Secret |
|-----|----------------|--------|
| Web app | Staff sign-in, colleague search, notification email | Client secret |
| PowerShell automation app | Disable the Entra account, change the mailbox, remove groups and licences | Certificate (`.cer` uploaded to Entra, `.pfx` stays on the VM) |

On-prem Active Directory is separate again. `LeaverProcess.ps1` disables the on-prem account with the Windows `ActiveDirectory` module. That module does not run on Linux. Cloud steps can run on this VM. The AD disable step still needs a Windows host that can reach a domain controller.

Keep **script test mode on** until Part 4. Turning it off disables real accounts.

## Part 1 — Let colleagues open the site

Entra only accepts the exact address the browser uses. `http://127.0.0.1:5044` is not a valid redirect URI. Use the server name.

On the VM:

```bash
sudo sed -i 's|http://127.0.0.1:5044|http://0.0.0.0:5044|' /etc/systemd/system/leaversform.service
sudo systemctl daemon-reload
sudo systemctl restart leaversform
```

From your PC, open `http://sh-pp-01:5044/`. If that host name does not resolve, use the VM's IP and substitute that IP everywhere this guide says `sh-pp-01`.

If the page does not load, allow the port from the office network:

```bash
sudo ufw allow 5044/tcp
```

The page should still show the banner **Development auth bypass is active**. That is expected until Part 2.

## Part 2 — Entra sign-in and colleague search

Use the existing Leavers Form web app in [Entra admin centre](https://entra.microsoft.com) if the Windows site already has one. Add redirect URIs to that app. Create a new registration only if there is no web app yet.

**App registrations → your web app → Authentication → Add a platform → Web** (not Single-page application).

Add these redirect URIs.

**Non-localhost must use HTTPS** in Entra (Microsoft no longer allows `http://` except for `localhost`). Put TLS on **nginx** (port 443), keep Kestrel on `127.0.0.1:5044`, and open the site as `https://your-dns-name/` (not `:5044`). See `deploy/linux/nginx-leaversform-ssl.conf.example`.

Example HTTPS URIs (hostname must match your certificate and `LeaversForm:BaseUrl`):

- `https://leavers.yoursclothing.co.uk/signin-oidc`
- `https://leavers.yoursclothing.co.uk/signout-callback-oidc`

Local dev only:

- `http://localhost:5044/signin-oidc`
- `http://localhost:5044/signout-callback-oidc`

On the same Authentication page, under **Implicit grant and hybrid flows**, tick **ID tokens**. Leave Access tokens unticked. Front-channel logout URL: `http://sh-pp-01:5044/signout-callback-oidc`.

**API permissions** (grant admin consent after adding them):

| Permission | Type | Used for |
|------------|------|----------|
| `User.Read` | Delegated | Sign-in |
| `User.Read.All` | Application | Colleague search on the submit form |
| `Mail.Send` | Application | Notification email |

From the app **Overview**, copy **Directory (tenant) ID** and **Application (client) ID**.

**Certificates & secrets → New client secret.** Copy the secret **Value** immediately. You cannot see it again.

On the VM, edit the settings file the running service actually reads:

```bash
sudo nano /opt/leaversform/app/appsettings.Development.local.json
```

Set the Azure AD values. Leave both test-mode flags `true`.

```json
{
  "AllowedHosts": "*",
  "AzureAd": {
    "TenantId": "YOUR-TENANT-ID",
    "ClientId": "YOUR-WEB-APP-CLIENT-ID",
    "ClientSecret": "YOUR-WEB-CLIENT-SECRET"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=leaversform.db"
  },
  "Graph": {
    "EmailTestMode": true,
    "RedirectAllEmailsTo": ""
  },
  "LeaversForm": {
    "BaseUrl": "http://sh-pp-01:5044",
    "EmailTestMode": true,
    "ScriptTestMode": true,
    "LogFolderPath": "/opt/leaversform/logs"
  }
}
```

Save a root-only copy, then restart:

```bash
sudo mkdir -p /etc/leaversform/secrets
sudo cp /opt/leaversform/app/appsettings.Development.local.json /etc/leaversform/secrets/appsettings.Development.local.json
sudo chown root:www-data /opt/leaversform/app/appsettings.Development.local.json /etc/leaversform/secrets/appsettings.Development.local.json
sudo chmod 640 /opt/leaversform/app/appsettings.Development.local.json /etc/leaversform/secrets/appsettings.Development.local.json
sudo systemctl restart leaversform
```

Check from your PC at `http://sh-pp-01:5044/`:

1. The browser goes to Microsoft sign-in. You are not signed in as Michael Newton.
2. After sign-in, the header shows your name.
3. **Submit a leaver** search finds a real colleague, not Alex Johnson, Sam Taylor, or Jordan Lee.

Sign in as `michaelnewton@yoursclothing.co.uk`, `davidlane@yoursclothing.co.uk`, or `vladkucherov@yoursclothing.co.uk` to see admin pages. Other accounts are normal users until a super-user adds them under **User roles**.

| What you see | What to fix |
|--------------|-------------|
| Still signed in as Michael Newton | `AzureAd:TenantId` is empty, or the service was not restarted |
| `AADSTS50011` redirect URI mismatch | The URI in Entra must match the address bar exactly, including `http` and `:5044` |
| `AADSTS700054` | ID tokens are not enabled, or the URI was added as a single-page app |
| Search is empty or shows the three fake names | `User.Read.All` is missing, or admin consent was not granted |

Do not submit a real leaver yet. Search only.

## Part 3 — Mailbox changes and Entra account disable (still test mode)

This is a **second** app registration. Name it `LeaversForm PowerShell Automation`. Supported accounts: this organisation only. Leave the redirect URI blank.

On the VM, create a certificate if `/opt/leaversform/LeaversForm-PowerShell-Automation.cer` is not already there:

```bash
sudo bash <<'EOF'
set -euo pipefail
install -d -o root -g www-data -m 750 /etc/leaversform/secrets
tmp="$(mktemp -d)"
openssl req -x509 -newkey rsa:2048 -sha256 -days 730 -nodes \
  -keyout "$tmp/key.pem" -out "$tmp/cert.pem" \
  -subj "/CN=LeaversForm PowerShell Automation"
openssl pkcs12 -export -out /etc/leaversform/secrets/automation.pfx \
  -inkey "$tmp/key.pem" -in "$tmp/cert.pem" \
  -password pass:LeaversForm-Cert-Backup
openssl x509 -in "$tmp/cert.pem" -outform DER \
  -out /opt/leaversform/LeaversForm-PowerShell-Automation.cer
chown root:www-data /etc/leaversform/secrets/automation.pfx
chmod 640 /etc/leaversform/secrets/automation.pfx
openssl x509 -in "$tmp/cert.pem" -noout -fingerprint -sha1
rm -rf "$tmp"
EOF
```

The last line prints the thumbprint (`AA:BB:...`). Remove the colons. That is `PowerShellCertificateThumbprint`.

Copy `/opt/leaversform/LeaversForm-PowerShell-Automation.cer` to your PC and upload it:

**Automation app → Certificates & secrets → Certificates → Upload certificate.**

Upload the `.cer` only. Leave `automation.pfx` on the VM. The PFX password is `LeaversForm-Cert-Backup`.

On the automation app, add **Application** permissions and grant admin consent:

| API | Permission |
|-----|------------|
| Microsoft Graph | `User.ReadWrite.All` |
| Microsoft Graph | `Group.ReadWrite.All` |
| Microsoft Graph | `Directory.ReadWrite.All` |
| Office 365 Exchange Online | `Exchange.ManageAsApp` |

`Exchange.ManageAsApp` is under **APIs my organization uses → Office 365 Exchange Online**, not under Microsoft Graph.

Then, in Exchange Online PowerShell, as an Exchange admin, register the app. The Object ID is the one on **Enterprise applications**, not the Object ID on the app registration page.

```powershell
Connect-ExchangeOnline
New-ServicePrincipal -AppId "YOUR-AUTOMATION-APP-CLIENT-ID" -ObjectId "YOUR-ENTERPRISE-APP-OBJECT-ID"
```

If `Connect-ExchangeOnline` later says the app is not authorised, assign the enterprise app the **Exchange Administrator** role in Entra → **Roles and administrators**.

Tell the web app about this certificate. In `/opt/leaversform/app/appsettings.Development.local.json`, add these fields under `LeaversForm` and keep `ScriptTestMode` true:

```json
"PowerShellTenantId": "YOUR-TENANT-ID",
"PowerShellAppId": "YOUR-AUTOMATION-APP-CLIENT-ID",
"PowerShellCertificateThumbprint": "THUMBPRINT-WITHOUT-COLONS",
"PowerShellCertificatePath": "/etc/leaversform/secrets/automation.pfx",
"PowerShellExchangeOrganization": "yoursclothing.onmicrosoft.com",
"PowerShellDomainController": "YOUR-DC.yours.local",
"PowerShellExecutable": "pwsh",
"PowerShellScriptPath": "/opt/leaversform/scripts/LeaverProcess.ps1",
"PowerShellWorkingDirectory": "/opt/leaversform/scripts"
```

Store the PFX password for the service:

```bash
sudo mkdir -p /etc/systemd/system/leaversform.service.d
sudo tee /etc/systemd/system/leaversform.service.d/override.conf >/dev/null <<'EOF'
[Service]
Environment=LeaversForm__PowerShellCertificatePassword=LeaversForm-Cert-Backup
EOF
sudo chmod 600 /etc/systemd/system/leaversform.service.d/override.conf
sudo cp /opt/leaversform/app/appsettings.Development.local.json /etc/leaversform/secrets/appsettings.Development.local.json
sudo systemctl daemon-reload
sudo systemctl restart leaversform
```

Confirm the log still says script test mode is on:

```bash
sudo journalctl -u leaversform -n 30 --no-pager | grep ScriptTestMode
```

You want: `ScriptTestMode is ENABLED — leaver accounts will NOT be disabled.`

Install the PowerShell modules the offboarding script uses, then do a dry run. `-WhatIf` must not change the mailbox or the account. On this VM the on-prem AD cmdlets will fail because the Windows module is missing. Graph and Exchange are the part you are checking.

After a git pull, sync scripts and app bits with:

```bash
sudo bash deploy/linux/setup-from-git.sh --skip-tests --bind-lan
```

Optional: import the PFX into the machine cert store (the script also reads `LEAVERSFORM_PS_CERTIFICATE_PATH` from your JSON when run via the web app or `test-leaver-whatif.sh`):

```bash
sudo bash deploy/linux/import-automation-pfx.sh /etc/leaversform/secrets/automation-new.pfx
```

Or use the helper (reads thumbprint and paths from `appsettings.Development.local.json`):

```bash
sudo bash deploy/linux/test-leaver-whatif.sh real.user@yoursclothing.co.uk SAMNAME
```

Manual `pwsh` example:

```bash
sudo pwsh -NoProfile -Command 'Set-PSRepository PSGallery -InstallationPolicy Trusted; Install-Module ExchangeOnlineManagement,Microsoft.Graph.Authentication,Microsoft.Graph.Users,Microsoft.Graph.Users.Actions,Microsoft.Graph.Groups,Microsoft.Graph.Identity.DirectoryManagement,Microsoft.Graph.Applications -Scope AllUsers -Force'
export LEAVERSFORM_PS_CERTIFICATE_PATH=/etc/leaversform/secrets/automation-new.pfx
export LEAVERSFORM_PS_CERTIFICATE_PASSWORD=LeaversForm-Cert-Backup
sudo pwsh -NoProfile -File /opt/leaversform/scripts/LeaverProcess.ps1 \
  -UserPrincipalName 'TEST-USER@yoursclothing.co.uk' \
  -SamAccountName 'TEST-SAM' \
  -TenantId 'YOUR-TENANT-ID' \
  -AppId 'YOUR-AUTOMATION-APP-CLIENT-ID' \
  -CertificateThumbprint 'THUMBPRINT-WITHOUT-COLONS' \
  -ExchangeOrganization 'yoursclothing.onmicrosoft.com' \
  -DomainController 'YOUR-DC.yours.local' \
  -WhatIf
```

Use a real test account you are allowed to inspect. Read the log under `/opt/leaversform/logs`. Certificate or consent errors stop the run before any change. An `ActiveDirectory` module error at the AD section is expected on Linux.

## Part 4 — On-prem AD

`LeaverProcess.ps1` resets the on-prem password, removes AD groups, and disables the AD account with `Disable-ADAccount`. That runs as the Windows account that starts PowerShell, not as the Entra app.

On a Windows server that can reach the domain controller:

1. Use a domain account that is allowed to reset passwords and disable users in the leaver OUs.
2. Install the RSAT Active Directory PowerShell module.
3. Run the same `-WhatIf` command as in Part 3.
4. Ping the domain controller from sh-pp-01 as well (`ping YOUR-DC.yours.local`). The Linux app records that name, but it cannot perform the AD writes itself.

Until that Windows run looks right, leave script test mode on. The Linux site can submit and track requests. It will not disable AD accounts.

## Part 5 — Send a real email, still without disabling anyone

Only after sign-in and search work. In the same JSON file set:

```json
"Graph": {
  "EmailTestMode": false,
  "SenderMailbox": "IT@yoursclothing.co.uk",
  "RedirectAllEmailsTo": "michaelnewton@yoursclothing.co.uk"
}
```

Set `LeaversForm:EmailTestMode` to `false` as well. Leave `ScriptTestMode` `true`.

Restart the service. Submit one test request. The message should arrive only in Michael Newton's mailbox, because every recipient is redirected there.

If Graph returns `ErrorAccessDenied` on send, Exchange is blocking this app from the IT mailbox. An Exchange admin needs an application access policy for the **web app** client ID and `IT@yoursclothing.co.uk`. Reuse the existing web app if that policy is already in place for the Windows site.

When you are finished with the mail test, set `RedirectAllEmailsTo` back to `""` only if you intend real recipients to get the mail.

## Part 6 — Go live

Do this last, after Parts 2 to 5, and only when you mean to disable real accounts.

In `/opt/leaversform/app/appsettings.Development.local.json`:

- `LeaversForm:ScriptTestMode` = `false`
- `LeaversForm:EmailTestMode` = `false`
- `Graph:EmailTestMode` = `false`
- `Graph:RedirectAllEmailsTo` = `""`

Restart:

```bash
sudo systemctl restart leaversform
sudo journalctl -u leaversform -n 20 --no-pager | grep ScriptTestMode
```

The line must say **ScriptTestMode is DISABLED**. A super-user can also switch this in the app under **Admin → Processing settings**. That switch takes effect without a restart.

Before the first real leaver:

- The Windows site must not process the same queue at the same time. Do not run `LeaversForm.Runner` as well.
- `LeaversForm:BaseUrl` is `http://sh-pp-01:5044` (or the real internal URL).
- One controlled test leaver, then read `/opt/leaversform/logs`.
- To stop real processing immediately, set script test mode back to `true` and restart, or use **Processing settings → Switch to TEST**.
