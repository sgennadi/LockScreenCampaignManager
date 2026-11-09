# LockScreen Campaign Manager 0.4.0-preview.81 — JPG Screen Saver current-user local install

Building on preview.80's real `PhotoScreenSaver.scr` and SHA-256-verified JPG package export, preview.81 lets you opt into a **current-user-only** pilot install after exporting the package.

## New Campaign Wizard Save actions

- **Install local JPG pilot...**: choose an exported JPG .scr folder, approve a default-No confirmation listing timeout and sign-in requirement. The manager verifies the package again, copies it into current-user LocalAppData and stores exact previous non-policy HKCU Desktop settings before setting the local `SCRNSAVE.EXE`, `ScreenSaveActive`, `ScreenSaveTimeOut`, and `ScreenSaverIsSecure` values.
- **Remove local JPG pilot...**: restore the previous settings and remove the local copy. If any of the four installed values changed in the meantime, refuse automatic rollback to avoid clobbering the user's or administrator's changes.
- If screen saver settings are managed by HKCU/HKLM Group Policy, the local installer refuses to override them. No UAC, HKLM writes, AD/SYSVOL modifications, scheduled tasks, or remote execution.

## Pilot, not production

Test actual idle activation, keyboard/mouse exit, sign-in on resume, multi-monitor layout, Windows Security and AppLocker/WDAC on a dedicated Windows 10/11 user session. Running .scr selftests on hosted GitHub workers does **not** prove those scenarios.

**Automatic JPG Screen Saver GPO deployment remains disabled.** Built-in Windows .scr User GPOs and existing Lock Screen clients continue unchanged.

## Automated checks

Windows win-x86/win-x64 self-contained EXE builds, offline contract suite, independent JPG .scr process selftest, hash-tamper rejection, and WinForms 100–200% scaling checks. Release ZIPs and SHA-256 digests are verified before publication.
