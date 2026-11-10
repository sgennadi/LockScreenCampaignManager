# LockScreen Campaign Manager — 0.4.0-preview.94

## English / עברית / Русский

This preview introduces **Russian** (LTR) alongside English (LTR) and Hebrew (RTL) in **Settings → Language**. It expands Hebrew into additional screens, including the dedicated Campaign Schedule editor.

**Russian interface coverage:** main editor and seven-step campaign wizard; AD Connection / alternate account; Computer/User group assignments; SYSVOL vs SMB network folder; PS1 vs EXE agent selection; GPO Deployment and Operations; Deferred Repair/gMSA; Client Health; diagnostics; schedule and image-folder matching; company profiles and history; advanced GPO inspection screens. A large initial Russian vocabulary is available, but untranslated UI strings still show English.

**Hebrew expansion:** campaign schedule, separate date/time editor, company profiles, group assignments, image matching and several advanced inspection/history screens.

**Language integrity:** original English captions and DataGridView/ListView header semantics are retained for reversible changes between Russian, Hebrew and English. ComboBoxes show translated choices **without replacing original enum, string or selection keys**. Domain names, GUIDs, credentials, OU/group DNs, computer names, UNC shares, Windows timezone IDs, dates, filenames, JSON/CSV/PowerShell and GPO command/policy data are not localized.

## Safety and acceptance

- Windows x64/x86 single-file binaries with four ZIP artifacts and a SHA-256 manifest, after Windows-hosted offline test and 100–200% simulated WinForms QA.
- These simulated tests are not a substitute for physical Windows 10/11 visual acceptance at real 200% display scaling or mixed-language screen-reader testing.
- No automatic SMB/AD/GPO mutation is introduced by localization. No silent credential persistence or automatic public issue upload.
- **Partial translation:** some dynamic validation text, advanced setup dialogs and diagnostic detail remain English. [Localization acceptance #36](https://github.com/sgennadi/LockScreenCampaignManager/issues/36) stays open.
- JPG Photo Screen Saver automatic User GPO rollout remains blocked pending isolated-domain client acceptance (#25/#26), and SMB startup/rollback evidence is still open (#30).

[Localization details](HEBREW-LOCALIZATION.md) · [Changelog](../CHANGELOG.md)
