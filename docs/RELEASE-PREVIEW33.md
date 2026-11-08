# LockScreen Campaign Manager 0.4.0-preview.33

## Usability and navigation
- Redesigned the main campaign editor to **start with images**, not AD sign-in. Any Windows user can prepare, arrange, preview and save a campaign offline.
- Made **Schedule** follow Add files/Add folder and remain unavailable until images exist. Wizard now follows Images → Schedule → Review & Save → Optional AD/GPO deployment.
- Reduced clutter with contextual **Project**, **Help / tools**, **Images / schedule**, **Review / export**, and **AD / GPO** menus. Administrative operations are separated from everyday local editing, with unavailable commands disabled.
- Replaced overflowing Next/Back buttons with a compact fixed-column navigation grid and text-only icons. Improved high-DPI button sizing and scrollable bottom menus.
- Added resizable image/preview split panes and persist the splitter ratio with main window position, size and maximized state.
- Kept Advanced manual operations, while maintaining read-only inventory and fresh GPO permission checks before deployment.
- Simplified AD Connection review: only read-only inventory and DC information is shown, not unselected OU/group/company placeholders. Clearer invalid-password error without flooding the status line.
- Kept optional per-user encrypted credential storage and signed/sealed LDAP connection.

## Verification
GitHub Actions performs Windows x64 and x86 portable self-contained builds, internal self-tests and offline AD/Windows 11 lab fixtures. These are not a substitute for tests on a real, delegated AD domain with Windows 11 clients, SYSVOL and group policy.

## Notes
The new layout prioritizes readable controls; window dimensions and splitter ratio are restored per user. Administrator permissions are **not** required for local campaign editing. GPO deployment still requires appropriate domain privileges and explicit user confirmation.

Portable EXEs are unsigned, and updates are manually initiated. Follow your organization's approval and antivirus policies.
