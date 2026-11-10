# LockScreen Campaign Manager 0.4.0-preview.92 — Hebrew AD/GPO and Client Health

Preview.92 extends the Hebrew/RTL interface from Preview.91 to the practical AD connection, account selection, Group Policy deployment and Client Health screens. This is a **partial interface localization**: it does not change deployment logic, security boundaries or saved project formats.

## Added UI translations

- **AD Connection Wizard:** connection, read-only inventory, review and permissions step labels; current/alternate Windows account options, DC discovery and common connection messages.
- **Alternate AD Account Wizard:** user search and password page headings, selected login, session-password explanations and search status messages. Passwords are never persisted by translation.
- **AD/GPO Deployment:** clearer Hebrew labels and warnings for writable DC, GPO creation and assignment; both PowerShell Startup Scripts and native EXE Startup Scripts remain distinct.
- **SYSVOL or SMB:** display-only Hebrew captions for the storage selector, while the raw item values and selected index stay unchanged. GPO metadata remains in SYSVOL.
- **Client Health / Live:** controls, fleet totals, diagnostic alert messages and displayed table headers. Original JSON report keys and English CSV column names are unchanged.
- **SMB feedback setup:** localized labels and buttons, with UNC and numeric intervals kept LTR.

Technical names, domains, computer IDs, OU/group DNs, GUIDs, hashes, UNC paths, date/time IDs and raw GPO/AD evidence are not translated. The window title keeps the exact software version. Switching languages does not trigger AD/GPO, SMB or endpoint work.

## Testing and limits

- GitHub-hosted Windows x86/x64 builds, full self-tests, Python offline contracts, six 100-200% WinForms simulated DPI/font profiles, and standalone ZIP/SHA-256 release verification are CI gates.
- **This is not evidence of live AD acceptance:** interactive Windows 10/11 Hebrew RTL screenshots, effective domain permissions, SMB startup connectivity and rollback need real-device testing.
- Not every advanced dialog, diagnostic value, or exception text is localized. See [Issue #36](https://github.com/sgennadi/LockScreenCampaignManager/issues/36).
- JPG photo slideshow **User GPO** auto-deploy remains blocked until isolated Windows/domain acceptance (#25/#26). SMB campaign startup and cleanup acceptance remains open (#30).

[Localization details](HEBREW-LOCALIZATION.md) · [Deployment](AD-DEPLOYMENT.md)
