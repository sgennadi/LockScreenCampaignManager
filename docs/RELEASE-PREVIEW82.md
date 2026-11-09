# LockScreen Campaign Manager 0.4.0-preview.82 — JPG Screen Saver GPO lab package

This release extends the previous local JPG Screen Saver proof of concept into a separate **manual Computer GPO Startup lab kit**. It deliberately does **not** activate JPG Screen Saver User GPO deployment.

## What's new

- Campaign Wizard **Save** now offers **Export GPO lab kit...** for JPG Screen Saver / Both. First export a JPG .scr package using **Export JPG .scr pilot...**. Then choose that exact campaign package and confirm. The new lab-kit folder is created beside it, without overwriting existing exports.
- The kit contains `PhotoSaver-StartupInstall.ps1`, a sibling folder containing the matching `PhotoScreenSaver.scr`, `slideshow.json` and JPGs, plus `LAB-GPO-INSTRUCTIONS.txt`.
- Export refuses a GPO lab payload larger than **256 MiB** to limit accidental SYSVOL replication load. Larger campaigns require separate, approved file distribution.
- The rendered Startup script pins the campaign ID, SHA-256 of the manifest and .scr and slideshow interval. It rechecks every photo, refuses linked/unsafe packages and installs under `C:\ProgramData\LSCM-PhotoSaver\<campaign-id>` with a restrictive ACL (SYSTEM/Administrators full, Users read+execute).
- Installation can only be initiated as LocalSystem by a **manually configured, dedicated TEST Computer Startup GPO**. `-Mode Verify` is read-only and checks the existing local installation. No automatic overwrite of a different package.
- No AD writes, SYSVOL copies, GPO creation or links, remote client tasks, HKCU writes or User GPO policies are performed by the export. This is a staging aid, not production delivery.

## Safe rollout sequence

1. Test the .scr directly on an isolated Windows 10/11 test desktop, including `/s`, `/p`, idle activation, password-on-resume and AppLocker/WDAC.
2. Export the GPO lab kit, read `LAB-GPO-INSTRUCTIONS.txt`, then manually scope a test computer Startup GPO to a dedicated tiny PC group and OU.
3. Stage script and sibling content together in that GPO's Startup script directory. Reboot ONE test PC, check hashes and ACL. Do not configure `SCRNSAVE.EXE` for a User GPO at this stage.
4. User Group Policy applicability, cross-OU target logic, local content availability and rollback still require validation before any JPG User GPO automation.

Built-in Windows Screen Saver User GPOs and existing Lock Screen clients are unchanged; JPG User GPO auto-deployment remains blocked. Group targeting is not loopback-enabled.

## Automated validation

Windows x64/x86 .NET builds; PowerShell 5.1 parser check for the new script; offline regression suite for GPO safety constraints; image package SHA-256 checks and immutable lab-kit export self-tests; WinForms 100–200% simulated scaling. Hosted CI cannot substitute for a real domain and client acceptance pilot.
