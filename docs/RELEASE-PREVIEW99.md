# LockScreen Campaign Manager — 0.4.0-preview.99

## What's new

1. **Repair results in three languages:** Repair completion summaries, task counters and confirmation dialogs now support English, Russian and Hebrew.
2. **Safer queue warnings:** If a remote Repair might have run but its result queue was not saved, a localized warning tells operators to inspect clients before retrying.
3. **GPO selection guidance:** Missing managed-GPO selection messages are translated in the lifecycle and remote-task preview screens.
4. **Language and DPI tests:** Regression checks verify that Russian/Hebrew labels round-trip to English and that numeric placeholders survive across simulated 100–200% WinForms scale profiles.
5. **Preview.98 preserved:** Numbered What's New, manual updater approval, ZIP architecture checks, SHA-256 verification and rollback safeguards remain unchanged.

**Safety:** This update changes only UI text, translation lookup and offline tests. It does not change GPO deployment, SMB, RPC, Repair queue records, client identity, history or automatic retries. Full physical Windows 10/11 200% RTL/LTR visual inspection (#36), SMB LocalSystem boot/rollback (#30), and JPG User Screen Saver GPO real domain pilot (#25/#26) still require separate acceptance.

[Changelog](../CHANGELOG.md) · [Localization tracking #36](https://github.com/sgennadi/LockScreenCampaignManager/issues/36)
