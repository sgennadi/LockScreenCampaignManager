# LockScreen Campaign Manager — 0.4.0-preview.95

## English / עברית / Русский: advanced operation screens

This preview extends the three-language UI beyond campaign creation into important operator workflows:

- **gMSA 24/7 Setup:** account, scope and SMB labels, actions, read-only diagnostics and explicit install/disable/remove confirmation dialogs now support Russian and Hebrew. The AD gMSA must already exist; no PowerShell/RSAT is introduced.
- **gMSA Production Readiness:** read-only title, actions, status, table headers, count labels and privacy-safe local JSON export confirmation. Internal check results and JSON evidence remain unchanged.
- **Device & Repair Dashboard:** controls, verified-device counts, read-only SMB/report status and guarded manual Repair warnings. Original device filter values, OU/group scope and full GPO GUID confirmation stay invariant.
- **SMB feedback and Client Health alerts:** translated explanations, thresholds and folder-creation warnings. UNC paths, ACLs, actual source files and per-user alert settings are not changed.
- **Layout:** translated buttons can grow at larger font sizes; six offline WinForms scale/font profiles exercise Russian LTR → Hebrew RTL → English LTR and preserved GPO/queue/filter/UNC values.

## Boundaries

- Partial localization: some updater dialogs, advanced exception details and live diagnostic evidence remain English intentionally or pending future work.
- Offline/CI success **does not** prove on-screen Windows lock-screen changes, domain GPO processing, SMB access under LocalSystem at boot or actual physical Windows 200% DPI text fit.
- No new automatic remote RPC/Repair, AD modification or background daemon is introduced by these UI changes. Approval dialogs remain default-No.
- JPG photo Screen Saver automated **User GPO** deployment remains blocked pending real Windows/AD acceptance (#25/#26), and SMB Windows startup/rollback acceptance (#30) remains open.

[Localization acceptance #36](https://github.com/sgennadi/LockScreenCampaignManager/issues/36) · [SMB acceptance #30](https://github.com/sgennadi/LockScreenCampaignManager/issues/30) · [Changelog](../CHANGELOG.md)
