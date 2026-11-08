# LockScreen Campaign Manager 0.4.0-preview.54

## Full guided six-step Wizard and DPI-safe typography
- **Start Wizard** and the main Wizard button now open an end-to-end guided flow: AD Connection (or offline), Images (multiple files or folder), Arrange, Schedule, Save and Deploy (or Finish offline).
- The AD connection remains an explicit read-only sign-in wizard; navigating between steps does not unexpectedly publish policies.
- Main LockScreen Campaign Manager header, AD Connection Wizard and Campaign Wizard grow the **outer container and inner title/step rows together** at higher DPI, with scrollable editor content.
- Offline Windows UI smoke tests construct every wizard page and inspect header sizing under four simulated font/DPI profiles. Interactive tests on actual 150-200% desktop scaling remain necessary.

## Update available indicator
- A nonblocking, read-only GitHub Releases check runs on open and, at most once every six hours, after subsequent app activations.
- A compatible newer release displays an amber **Update available: VERSION** indicator and tooltip containing release date, installed version and brief release notes.
- Click to open Updater (which checks again); retains the two visible steps **1 Check / 2 Install** and a separate user-approved integrity-verified install. No automatic download or install; timeouts/errors never claim up-to-date status.
- Also shows a compact update button on the Campaign Wizard.

## Native C# Immediate Actions (read-only)
- **List eligible computers** and **Preview** now use native signed/sealed LDAP and selected-controller SYSVOL, with no PowerShell launched for these two read-only modes.
- Eligibility is limited to selected OU and recursively nested security group membership. Preview accepts only 1-25 deduplicated, in-domain AD computer names.
- Preview revalidates the managed GPO, OU links, campaign manifest and schedule, then calculates the same SHA-256 approval hash consumed by the existing Task Scheduler RPC engine.
- Adds standalone C# self-tests plus an independent PowerShell 5.1 regression that extracts the actual legacy `$hashInput` AST expression to verify the cross-engine digest. Nothing changes on clients during Preview.

## Not yet migrated
- Immediate Actions **Push / Repair / Report / Cleanup**, GPO Lifecycle **Apply**, GPO Deployment, SYSVOL actual **Cleanup**, extended effective-policy verification and remote VerifyCleanup still use their existing guarded PS1 engines.
- Windows client LockScreen-Agent.ps1 stays unchanged. Scheduled background gMSA worker still rechecks AD and eligibility before attempting repair.

## Validation
CI validates x86/x64 portable EXE, update metadata contracts, offline policy/approval hashes and WinForms smoke cases. It cannot validate real domain replication or live RPC changes. Test on a dedicated AD OU and computer/group before production use.
