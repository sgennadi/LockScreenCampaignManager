# LockScreen Campaign Manager — 0.4.0-preview.109

## What's new

1. **Remote session GPO blockers:** Identify configured RDS idle, total connection, disconnected-session time limits and end-session action in both Computer and User policy sources.
2. **Correct timeout comparison:** Compare RDS idle time in milliseconds with the configured Screen Saver idle time in seconds, marking short/equal remote-session limits as conditional warnings; do not imply a local console conflict.
3. **Compare GPO settings:** New read-only action from **Scan all DC GPOs** to compare the same exact registry key/setting/scope across different GPO GUIDs, including equal, different and ambiguous source values.
4. **Readable trilingual audit:** Search and filter GPO collisions, review source GPOs/values and potential link overlap, with English/Russian/Hebrew display labels and confirmed local-only, formula-safe CSV export.
5. **Reliability checks:** New C# regressions, Python offline safety assertions, Windows x86/x64 build gates and simulated 100–200% UI layout testing in the existing CI pipelines.

**Limits:** RDS is for Remote Desktop sessions, not console screens. Multiple matching GPO source files are not proof that those policies ever apply to the same user/device or which wins. The scanner still recognizes a bounded subset of GPO settings, not arbitrary CSEs. Confirm scope, security/WMI/GPP filtering and actual user/computer RSoP with gpresult on real Windows 10/11 clients. Neither domain policy nor endpoint is modified.

[All-GPO and OU scope guide](OU-AND-SECURITY-GROUP-TARGETING.md) · [Changelog](../CHANGELOG.md)
