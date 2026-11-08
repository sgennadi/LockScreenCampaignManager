# LockScreen Campaign Manager 0.4.0-preview.31

Windows 11 / Active Directory lock screen campaign manager public preview.

## Changes
- Fix Update Manager HTTP 404 when the public GitHub repository contains only prereleases: Stable-only now returns a clear no-stable-release message; Preview + stable checks both channels without a token.
- Replace the first AD connection dialog with a three-step BitKeyBridge-style wizard: Connect to DC, inspect signed/sealed read-only directory inventory, review and confirm. No GPO changes happen during discovery.
- Automatically discover controller FQDNs from Windows DNS SRV records when an AD DNS domain is found. Expanded DC selector (750px minimum dropdown) and full-name tooltips. Manual DC selection remains possible.
- LockScreen window header space and minimum size expanded for high DPI. Add a custom Windows EXE/form icon and consistent vector glyphs on actions.
- Add separate **Add Files...** multi-select and **Add Folder...** image imports with JPG/JPEG/PNG/BMP support, duplicate/symlink/empty-file checks and schedule review.
- Both x86/x64 GitHub-hosted Windows builds run application self-tests, GUI layout smoke, UniversalUpdater private/public synthetic transfers and UniversalDiagnostics/Debug checks before publication.
- SHA-256 manifests and single-executable ZIPs supplied for verified manual updates.

## Important
This is a **preview** release. Real production DC, GPO, gMSA and Windows 11 endpoint changes have not been exercised by GitHub-hosted offline tests. Updates remain manual and unsigned executables are not automatically installed. Review deployment scope and existing GPO policies in a lab OU first.

## Assets
- `LockScreenCampaignManager-0.4.0-preview.31-win-x64.zip` / `-win-x86.zip`: portable manager EXE
- `LockScreenCampaignManager-Update-win-x64.zip` / `-win-x86.zip`: UniversalUpdater-compatible single-file ZIP
- `SHA256SUMS.txt`: archive digests
