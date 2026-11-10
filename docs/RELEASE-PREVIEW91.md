# LockScreen Campaign Manager 0.4.0-preview.91 — Hebrew Campaign Wizard

Preview.91 extends the partial English / עברית interface added in Preview.90, focused on the operator's **seven-step Campaign Wizard**. This build does not change AD/GPO execution logic, saved project schema, or the existing experimental Photo Screen Saver safeguards.

## Campaign Wizard localization

- Main step headings, supporting instructions and step rail for AD Connection, Campaign Type, Images, Arrange, Schedule, Save/Skip and Deploy are localized in Hebrew.
- Translate static actions and common live status messages, including Screen Saver choices, JPG slideshow controls, SYSVOL/SMB storage hints, saved-project warnings and AD connected/offline descriptions.
- Translate **only the visible text** of the campaign type and content-location pickers. Internal enum values, ComboBox items, selected index, JSON keys, campaign names, Windows timezone IDs, file names, LDAP DN, UNC path and hashes are never rewritten.
- WinForms RTL mirroring for Hebrew; technical path/report text remains LTR. Wizard action buttons can grow for translated text and page paragraphs expand into the scrollable area rather than clipping at larger UI scales.
- Settings → Language → **עברית (Hebrew)** or **English** can be changed and returned without altering project source settings. Existing English installations still default to English.

## Testing and safety

- GitHub Actions x64/x86 CI includes **all seven offline WinForms wizard pages** in Hebrew and English, plus static-contract regression tests for enum/source stability.
- Hosted UI tests are simulated font/DPI tests; real interactive Windows 10/11 200% RTL visual acceptance is not yet complete.
- No domain, SMB, SYSVOL, GPO or LocalSystem actions are performed by language/layout tests.
- Some advanced dialogs, validation messages, diagnostics, date and time subdialogs and dynamic technical status remain in English; this is **not** a complete Hebrew UI.
- JPG Photo Screen Saver automated User GPO remains blocked pending isolated domain testing (#25/#26); SMB content deployment and rollback require actual endpoint acceptance (#30).

See [Hebrew localization details](HEBREW-LOCALIZATION.md) and [open localization acceptance issue](https://github.com/sgennadi/LockScreenCampaignManager/issues/36).
