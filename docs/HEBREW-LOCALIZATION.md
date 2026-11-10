# Hebrew user interface — iterative localization (Preview.93)

Use **Settings → Language → עברית (Hebrew) → Apply**. Language is stored in the existing per-user `ui-settings.json` beside density, UI scale and font size. No AD privileges, additional installation or language packs are required.

**Covered in Preview.90:** most primary navigation buttons, menu labels, Wizard actions, Settings, key software-update controls, GPO Operations tabs, and the Local Lab Evidence viewer. Main windows use WinForms RTL mirroring. Technical text/values (UNC, LDAP, GPO GUID, logs, JSON, machine names, hashes, report summary) stay LTR to avoid corrupting their perceived order.

**Expanded in Preview.91:** The seven-step Campaign Wizard now translates main headings and explanations, step rail, live navigation, Screen Saver mode options and common status, and Save/Deploy SYSVOL-vs-SMB selection. Mode enums and SYSVOL/SMB ComboBox items retain their original values internally; only the visible captions are localized. Wrapped text rows grow within a scrollable wizard page; the bottom navigation bar remains separate.

**Expanded in Preview.92:** the AD Connection Wizard, alternate user/password search, AD/GPO deployment steps, SYSVOL/SMB and PS1/EXE selection labels, Client Health summary/header and SMB feedback controls have Hebrew captions. Internal enum values and client report CSV/JSON keys remain English. Application title captions preserve the version and company name; technical identities and evidence remain left-to-right. All six simulated DPI/font profiles exercise Hebrew → English reversal without changing data or talking to AD.

**Expanded in Preview.93:** GPO Operations now includes more Hebrew titles, actions and hints for agent management, deferred repairs, gMSA repair worker and static GPO conflict findings. Diagnostics review, local trend analysis and privacy confirmation are more readable in Hebrew. GUI-only ComboBox formatting and grid header translation never modify selected operation IDs, AD activity filters, risk/queue states, JSON/CSV report schemas or GUID confirmation input. Six simulated font/DPI profiles check a reversible English/Hebrew switch.

**Partial translation by design:** some advanced AD/GPO dialogs, validation details, raw diagnostic evidence, the separate schedule editor, some Client Health text and additional dynamic status messages remain English. JPG photo saver deployment still requires real Windows/AD pilot acceptance. A missing translation is displayed in English rather than being silently changed. Restart/reopen a dialog after changing language if a status was generated before the switch.

English is the default. Switching back to English is reversible. This setting changes **UI presentation only**, not campaign schedules, timezone IDs, serialized JSON, agent package structure, Active Directory, GPO behavior or shell script output.

Real interactive 200% DPI/RTL visual testing is recommended in addition to the six offline font-scale profiles used by CI.
