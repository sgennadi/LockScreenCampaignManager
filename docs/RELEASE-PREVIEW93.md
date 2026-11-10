# LockScreen Campaign Manager — 0.4.0-preview.93

## Operator-facing Hebrew UI extension

- **GPO Operations:** more of the managed GPO lifecycle, read-only conflict checking and Agent Management screens have English/עברית captions. The operation IDs and consent gates remain unchanged.
- **Deferred Repair and gMSA 24/7:** translated tab headings, actions, hints and displayed queue table headers. Never translate persisted queue status, retry logic, machine identity or credentials.
- **Full conflict report:** Hebrew operator controls, risk filter display and table headers, with **exact original CSV column names** and policy evidence. Mandatory private-data warning remains.
- **Diagnostics:** English/עברית titles and principal controls for reviewing saved errors, local trend analysis, aggregate summary and user-mediated GitHub reporting. A public GitHub Issue still requires review and explicit submission; no automatic upload is added.
- **RTL/DPI:** static UI metadata can switch Hebrew ↔ English while GUID, domain DN, UNC, raw JSON, logs and report contents retain LTR orientation. Longer buttons grow and toolbars remain scrollable.

This is a **partial** Hebrew interface. Some advanced dialogs, error and validation details and raw technical evidence remain English. We do not describe this as a complete translation. Interactive Windows 10/11 acceptance at physical 200% scaling is still required.

## CI and production safety

Windows win-x64 and win-x86 CI must compile and pass full self-tests, source contracts and six WinForms UI scales. Tests never connect to customer AD, execute remote tasks, modify SYSVOL, export to public GitHub or deploy JPG User GPO. Real SMB endpoint and JPG photo Screen Saver rollout gates remain open.

[Localization tracking #36](https://github.com/sgennadi/LockScreenCampaignManager/issues/36) · [SMB acceptance #30](https://github.com/sgennadi/LockScreenCampaignManager/issues/30) · [JPG User GPO acceptance #26](https://github.com/sgennadi/LockScreenCampaignManager/issues/26)
