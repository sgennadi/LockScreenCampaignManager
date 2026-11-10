# LockScreen Campaign Manager — 0.4.0-preview.96

## English / Русский / עברית: updater and local evidence

**Portable Updater:** translated read-only Check results, version comparison labels, Preview/Stable descriptions, package verification and progress, unsigned-publisher warning, explicit install consent (default No), backup/.bak inspection and rollback cautions. Package hashes, untrusted release notes, archive names, version, GitHub repository and session token are never translated. No background auto-install or gMSA auto-replacement is introduced.

**Local Windows Lab Evidence:** translated privacy reminders, local file-picking prompts, import/invalid report status and rejection warnings. The imported JSON is strictly validated offline, cannot authenticate a physical endpoint, and raw technical evidence is never translated or uploaded.

**Safety and layout:** all three languages are tested on six simulated WinForms scale/font profiles (100–200%). Reversible LTR Russian/English ↔ RTL Hebrew is tested without changing updater channel selected-index, original data, or LTR technical fields. Live Windows 10/11 200%-DPI visual inspection and domain tests were not performed.

**Open acceptance gates:** Hebrew/Russian dynamic coverage is still partial (#36); SMB LocalSystem startup/rollback remains unverified on live Windows 10/11 (#30); automatic JPG User Screen Saver GPO rollout remains blocked pending pilot (#25/#26).

[Localization issue #36](https://github.com/sgennadi/LockScreenCampaignManager/issues/36) · [Changelog](../CHANGELOG.md)
