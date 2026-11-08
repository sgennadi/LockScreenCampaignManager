# LockScreen Campaign Manager 0.4.0-preview.30

Public preview for the vendor-neutral .NET 10 Windows Forms management tool.

- AD-first discovery: connect to a chosen domain controller, read OUs, computer groups, GPO metadata, then configure campaigns. Invalidate cached snapshots when the company, DC, OU, group, or connection changes.
- BitKeyBridge-style navy/blue/green layout with per-monitor DPI support.
- UniversalUpdater: optional read-only token for private sources; the public GitHub repository supports anonymous release checks and downloads. Synthetic tests cover metadata, asset ID, CDN redirect, SHA-256 ZIP integrity, PE architecture and unsigned Authenticode warnings. Automatic installation remains disabled.
- UniversalDiagnostics: local redacted report preview, manual public GitHub Issue submission and synthetic privacy/logging checks. Nothing is uploaded automatically.
- GitHub Actions tests include Windows x64/x86 builds, real WinExe exit-code validation, full and focused self-tests, PowerShell parser/integrity checks, and GUI layout smoke tests.

**Preview/testing limitations:** The code has not been validated against a production DC, actual GPO application, gMSA deployment or physical Windows 11 lock screen. Portable EXEs are not guaranteed Authenticode-signed; confirm publisher trust before executing. Use a lab OU and backup existing GPOs first.

## Packages

- `LockScreenCampaignManager-0.4.0-preview.30-win-x64.zip` and `-win-x86.zip`: portable packages.
- `LockScreenCampaignManager-Update-win-x64.zip` and `-win-x86.zip`: one-EXE ZIPs validated by UniversalUpdater.
- `SHA256SUMS.txt`: SHA-256 of the four ZIP files.

Use the **Preview + stable** update channel to see preview builds. Stable only intentionally skips this prerelease.
