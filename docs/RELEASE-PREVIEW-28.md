# 0.4.0-preview.28 — LockScreen Campaign Manager

**Prerelease for administrator evaluation. Not an approved production/stable build.**

## What's new since preview.27

**Start with Step 1 — Connect to DC & Read AD**, following the BitKeyBridge connection-first workflow. An interactive first-step window opens at startup. Select a DNS domain and preferred DC, with either the current Windows account or an alternate domain account (password is session-only and never stored).

- Read-only signed and sealed LDAP/RootDSE snapshot shows **domain, DC, writable/read-only state, domain controllers, OUs, computer security groups and existing GPO names**.
- View counts and sample entries before opening the campaign editor; flag truncated inventories. **2 Continue to Campaigns** is enabled only after successful fresh same-domain inventory. **Work offline** is explicitly available without GPO permissions.
- **3 GPO Preview / Deploy** requires a recent read-only Step 1 snapshot plus campaign images, then performs its *own* fresh LDAP/OU/group/permission verification and read-only preview before any explicit GPO create confirmation.
- BitKeyBridge-inspired **navy headings, blue actions, green connection success and pale blue/green panels**, DPI/high-contrast support and durable per-window placement.
- Incomplete campaigns such as `Add at least one image.` are treated as actionable input validation, with a direct option to select images, instead of a crash telemetry report.
- The manual GitHub Issue option clearly explains private-repository 404, browser authentication and the fact that copying diagnostics does **not** submit an issue; `delivery.status: pending` remains honest until user submission.

Includes the previous preview.27 cached Registry Policy RSoP source, managed JPEG SHA-256 evidence, GPO conflict analyzer and deferred repair handling.

## Download assets

- `LockScreenCampaignManager-0.4.0-preview.28-win-x64.zip` — full portable x64 self-contained manager.
- `LockScreenCampaignManager-0.4.0-preview.28-win-x86.zip` — full portable x86 self-contained manager.
- `LockScreenCampaignManager-Update-win-x64.zip` — single EXE updater package.
- `LockScreenCampaignManager-Update-win-x86.zip` — single EXE updater package.
- `LSCM-Offline-AD-Win11-Test-Kit-0.4.0-preview.28.zip` — isolated AD/Windows 11 pilot preflight toolkit.
- `SHA256SUMS.txt` — SHA-256 hashes for the five ZIPs.

EXEs are self-contained and don't require an installed .NET runtime. They are **not Authenticode-signed**. Private GitHub repository access is required to download.

## Verification and limitations

Windows GitHub-hosted CI: PowerShell 5.1 parse + safety contracts, C# self-tests for scoped read-only AD snapshots and campaign validation, WinForms/DPI simulation, Windows x64/x86 publish and archive inspection. The release pipeline rebuilds independently and validates every GitHub-hosted asset size and digest before publishing.

**Not tested against your real DC, Active Directory permissions, GPO propagation, client Windows 11, gMSA operation or physical Lock Screen.** Hosted Actions cannot reach the private domain. Test on a pilot domain-joined workstation, confirm readable directory inventory and independently test GPO creation in a disposable OU before production use.

No changes to stable release or existing preview.27. No automatic GPO deployment or diagnostics upload.

Technical details: [AD-first connection](docs/AD-FIRST-CONNECTION.md) · [RSoP CSE / SHA-256 evidence](docs/RSOP-CSE-IMAGE-INTEGRITY.md).
