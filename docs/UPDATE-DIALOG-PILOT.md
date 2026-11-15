# Update confirmation and HKCU Screen Saver pilot

## Install approval

The updater's **Install** action now shows six short, fully visible instructions: archive verified by SHA-256; unsigned publisher; local EXE backup and restore on failed offline self-test; existing gMSA/admin installations not replaced; save unsaved campaign changes; confirm installation. The default answer is **No**. Esc, X and No cannot trigger installation.

This does not change the existing portable updater's safety checks or elevate installation rights. A SHA-256 checksum is not an Authenticode signature and an unsigned release cannot establish publisher identity. Use only releases that you independently recognize and trust.

## Endpoint Screen Saver HKCU reading

The probe reads HKCU of the **actual Windows account running the program**, not another selected AD user. It labels each registry value with its source:

- `PolicyHKCU` — per-user policy registry key
- `DesktopHKCU` — per-user desktop registry key
- `RestrictionHKCU` — the Screen Saver page restriction key

When SCRNSAVE.EXE is absent from both sources, local file check `NotSelected` is expected: no executable was chosen for local byte-open verification. This is **not** a conclusion that the Screen Saver GPO is misconfigured or that no other Windows/default policy could influence behavior.

For accurate troubleshooting, select the exact pilot computer/OU and AD user, run **Analyze AD scope (no XML)**, import fresh HKCU evidence captured as that same user's SID, and compare power timings. Effective GPO, User RSoP, MDM, session context and actual Screen Saver display still need real Windows pilot verification. Do not share unredacted HKCU JSON publicly.
