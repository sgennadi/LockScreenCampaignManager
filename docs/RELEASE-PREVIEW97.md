# LockScreen Campaign Manager — 0.4.0-preview.97

## Three-language safety prompts — English / Русский / עברית

**GPO candidates & RSoP:** read-only gpresult XML import, historical RSoP warning, native HKLM remote inspection, combined cached RSoP/JPEG SHA-256 source check, and private local GPO CSV exports now offer Russian and Hebrew safety descriptions. All existing operator approvals remain default-No. GPO/OU/group/DC identity is revalidated before an approved endpoint read. Neither remote observations nor imported RSoP prove a physical on-screen lock screen.

**Client Health:** SMB browsing/invalid-root warning, stale or foreign JSON notices, Repair selection/scope validation, final campaign snapshot and SMB share permissions confirmations are translated. No UNC paths, client JSON, original CSV headers, hash strings or alert telemetry values are rewritten. A final report is not proof that every endpoint completed.

**Campaign Closure Audit & GPO Operations:** translated read-only post-cleanup RPC/SMB/registry consent and progress, historical closure counters, private report exports and remote task/RSoP candidate statuses. Manual remote actions still require verified AD targets and the existing full GPO GUID approval.

**Windows QA:** Windows x86 and x64 build/self-tests and six simulated 100–200% WinForms scaling profiles test Russian LTR → Hebrew RTL → English LTR reversibility. This does not substitute for an actual Windows 10/11 desktop 200% visual inspection, domain GPO/LocalSystem startup testing, or screen-saver activation acceptance.

**Open gates:** incomplete localized advanced exception messages and real RTL/DPI testing (#36); SMB LocalSystem boot/rollback (#30); JPG User Screen Saver GPO real AD pilot (#25/#26).

[Localization tracking #36](https://github.com/sgennadi/LockScreenCampaignManager/issues/36) · [Changelog](../CHANGELOG.md)
