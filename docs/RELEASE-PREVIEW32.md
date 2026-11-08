# LockScreen Campaign Manager 0.4.0-preview.32

Windows 11 / Active Directory lock screen campaign manager public preview.

## Changes
- Rework the first Active Directory wizard into **user-first authentication**, modeled on BitKeyBridge: current Windows account or a different AD user, signed/sealed LDAP inventory, then review and confirm.
- Keep domain DNS prominent; move optional DC override into Advanced connection options. Repair DNS SRV parsing and add Windows DC Locator fallback.
- Add independent, opt-in **Remember username** and **Save encrypted password** checkboxes for alternate AD accounts. The latter uses the per-Windows-user Windows Credential Manager. Saved login can be removed from the wizard. Credentials are never added to campaign project files or CI diagnostics.
- Fix verified directory snapshot invalidation when clearing an ephemeral password after authentication.
- Replace the silent **New campaign** reset with a named campaign creation dialog, including start date and time.
- Improve DPI-aware AD DNS/DC form layout and button labels.
- Fix seven distinct compiler warnings and update WinForms smoke tests for renamed sign-in controls.
- Pin preview publishing CI to Ubuntu 24.04 to avoid the ubuntu-latest migration notice.

## Security and validation
Passwords are only saved after an explicit opt-in and successful connection; the stored credential is scoped to the current Windows user account. Other processes running under that same Windows user may access their Credential Manager secrets. Do not save privileged AD accounts on shared or untrusted workstations.

Both Windows x64/x86 builds and offline tests run in GitHub Actions. Actual production AD domain, gMSA, Group Policy, and Windows 11 endpoint integration remain unverified without a domain-connected test environment. Unsigned builds remain opt-in/manual.

## Assets
- `LockScreenCampaignManager-0.4.0-preview.32-win-x64.zip` / `-win-x86.zip`: portable EXE
- `LockScreenCampaignManager-Update-win-x64.zip` / `-win-x86.zip`: single-file updater ZIP
- `SHA256SUMS.txt`: archive digests
