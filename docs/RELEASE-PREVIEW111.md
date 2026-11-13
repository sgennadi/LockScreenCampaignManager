# LockScreen Campaign Manager — 0.4.0-preview.111

## What's new

1. **Pilot readiness:** New read-only checklist opened from **Scan all DC GPOs → Compare gpresult XML...**.
2. **Combined evidence:** Review DC-wide GPO source candidates, independent Computer/User gpresult XML and optional live computer power readings without calling any setting a confirmed winning GPO.
3. **Power conflicts:** Compare configured Screen Saver idle time against actual observed display-off, sleep and machine inactivity lock values, including AC/DC and disagreements between WMI and policy registry.
4. **Safe client check:** Explicit **Verify power (PC)...** confirmation, matching selected Computer OU/FQDN against the chosen DC, no automatic endpoint contact or changes.
5. **Improved usability and tests:** RU/HE control labels, high-DPI layout, source-integrity/stale-data regression checks and Windows x64/x86 plus Offline AD Lab CI.

**Important:** Imported gpresult is unsigned, and may describe a different user/machine/time. Per-setting RSoP, signed-in User HKCU, actual Screen Saver display/resume and visual Windows 10/11 behavior are **NOT verified** by this report. No changes to AD, GPO, SYSVOL, clients or power state, and no automatic repair. A real isolated domain pilot is still required.

[User/Computer RSoP and pilot guide](RSOP-EVIDENCE-IMPORT.md) · [Changelog](../CHANGELOG.md)
