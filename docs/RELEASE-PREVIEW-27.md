# 0.4.0-preview.27 — LockScreen Campaign Manager

**Prerelease for administrator testing. Not an approved production/stable build.**

## Changes since preview.26

- **Verify RSoP + image** in **GPO Operations → Lock Screen conflicts → Analyze effective (PC)**.
- Read-only computer Registry Policy CSE `RSOP_RegistryPolicySetting` snapshot through remote WMI/DCOM, with scoped GPO GUID and `precedence=1` as a **cached CSE-reported source**.
- Read-only `LockScreenImage` registry observation and SHA-256 of the *specific managed campaign JPEG* over administrative SMB, compared with the campaign manifest on the selected DC.
- Explicit unknown/ambiguous/missing/unreadable/hash-mismatch states; no claim that a physical lock screen image is visible.
- Per-setting report with searchable WinForms/DPI layout, local opt-in CSV and conservative scope checks.
- Preview manager, embedded agent and updater share version `0.4.0-preview.27`.

## Included assets

- `LockScreenCampaignManager-0.4.0-preview.27-win-x64.zip` — portable full manager for Windows x64
- `LockScreenCampaignManager-0.4.0-preview.27-win-x86.zip` — portable full manager for Windows x86
- `LockScreenCampaignManager-Update-win-x64.zip` — **single-EXE** archive for the embedded UniversalUpdater
- `LockScreenCampaignManager-Update-win-x86.zip` — **single-EXE** archive for the embedded UniversalUpdater
- `LSCM-Offline-AD-Win11-Test-Kit-0.4.0-preview.27.zip` — manual AD/Windows 11 pilot preflight kit
- `SHA256SUMS.txt` — digest of each ZIP

All release binaries are self-contained .NET 10 Windows executables and require no installed .NET runtime. They are **not Authenticode signed**; administrator-installed update safeguards remain enabled. Private GitHub repository access is required to download assets.

## Verification performed

Hosted Windows x86/x64: PowerShell 5.1 syntax and safety-contract tests, synthetic RSoP/CSE and hash fixtures, portable EXE self-tests, WinForms UI/layout smoke, packaged ZIP structure and checksum validation.

**Not performed:** authentic tests on this organization's Domain Controller, real Windows 11 endpoints, WMI/DCOM/SMB access permissions, GPO propagation, physical lock-screen display or gMSA operation. GitHub-hosted runners cannot access the private domain. Do not roll out to production until an isolated pilot completes successfully.

## First-run test

Extract the **x64** or **x86** portable package, launch `LockScreenCampaignManager.exe` on an authorized management computer, and begin with read-only conflict analysis. To perform a real AD/Win11 pilot, extract the separate offline lab kit, configure its **example-only** configuration for an isolated lab OU, then run **Run-Preflight.cmd**; **Run-Full-Lab.cmd** is intentionally disruptive and requires separate approval and disposable clients.

Release notes and technical boundaries:
- [RSoP CSE + Image Integrity](docs/RSOP-CSE-IMAGE-INTEGRITY.md)
- [Offline AD/Windows 11 Lab](docs/OFFLINE-AD-WIN11-LAB.md)
