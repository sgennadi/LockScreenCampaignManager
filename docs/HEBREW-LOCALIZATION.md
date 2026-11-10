# Hebrew user interface — iterative localization (Preview.91)

Use **Settings → Language → עברית (Hebrew) → Apply**. Language is stored in the existing per-user `ui-settings.json` beside density, UI scale and font size. No AD privileges, additional installation or language packs are required.

**Covered in Preview.90:** most primary navigation buttons, menu labels, Wizard actions, Settings, key software-update controls, GPO Operations tabs, and the Local Lab Evidence viewer. Main windows use WinForms RTL mirroring. Technical text/values (UNC, LDAP, GPO GUID, logs, JSON, machine names, hashes, report summary) stay LTR to avoid corrupting their perceived order.

**Expanded in Preview.91:** The seven-step Campaign Wizard now translates main headings and explanations, step rail, live navigation, Screen Saver mode options and common status, and Save/Deploy SYSVOL-vs-SMB selection. Mode enums and SYSVOL/SMB ComboBox items retain their original values internally; only the visible captions are localized. Wrapped text rows grow within a scrollable wizard page; the bottom navigation bar remains separate.

**Partial translation by design:** some advanced AD/GPO dialogs, error and validation details, diagnostics, the separate schedule editor, client health text and some dynamic status messages remain English. JPG photo saver deployment still requires real Windows/AD pilot acceptance. A missing translation is displayed in English rather than being silently changed. Restart/reopen a dialog after changing language if a status was generated before the switch.

English is the default. Switching back to English is reversible. This setting changes **UI presentation only**, not campaign schedules, timezone IDs, serialized JSON, agent package structure, Active Directory, GPO behavior or shell script output.

Real interactive 200% DPI/RTL visual testing is recommended in addition to the six offline font-scale profiles used by CI.
