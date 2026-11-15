# LockScreen Campaign Manager — 0.4.0-preview.123

## What's new

1. **Update approval fits the dialog.** The portable updater now presents a concise six-line security/backup confirmation instead of a long scrolled warning. The user still explicitly chooses Yes; **No**, Esc and window close do not approve the update.
2. **Unsigned publisher is explained accurately.** GitHub release SHA-256 checks prove that the downloaded archive matches its expected bytes, **not** the developer's signing identity. Preview EXE files may lack Authenticode verification. The operator decides whether to trust the official project release; no automatic approval is granted.
3. **Readable Screen Saver diagnostic summary.** The signed-in-user HKCU probe shows the available ScreenSaveActive, ScreenSaveTimeOut, SCRNSAVE.EXE and ScreenSaverIsSecure values and their PolicyHKCU/DesktopHKCU sources before the complete private JSON.
4. **No false claims from absent settings.** `NotSelected (None)` is explained as no local .scr/.exe path being present. Missing HKCU values are labeled as **not configured in the captured HKCU**, not as confirmed GPO failure or proof that Windows cannot display a Screen Saver. Actual User RSoP, defaults and interactive-session behavior remain unverified.
5. **Russian, Hebrew and offline UI regressions.** Add localized snapshot captions and native/offline safety checks. Preserve the existing updater's no-overwrite, rollback, self-test, and manual approval requirements.

## Pilot observations (2026-11-15)

An operator-provided Windows 10 domain test JSON contained `DesktopHKCU ScreenSaveActive=1`, no ScreenSaveTimeOut, no SCRNSAVE.EXE, no ScreenSaverIsSecure, no Screen Saver-related PolicyHKCU readings, and `scrFile=NotSelected`. This is sufficient to **report missing values**, but insufficient to prove successful Screen Saver launch or a conflicting GPO. Recheck after selecting the exact AD user and scanning its GPO scope; inspect interactive Windows behavior.

## Safety

The updater remains user-approved and retains existing SHA-256, archive validation, backup/restore and offline self-test gates. This release does not change AD, GPO, SYSVOL, registry values, Screen Saver files or deployment tasks. The HKCU JSON remains private, unsigned, and intentionally not uploaded by the probe.
