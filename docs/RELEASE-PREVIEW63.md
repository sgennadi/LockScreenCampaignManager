# LockScreen Campaign Manager 0.4.0-preview.63

## Connection wizard: reliable DC write-capability and color-coded permissions

- The AD Connection Review now highlights connection facts, advisory ACL permission hints, warnings, and verified errors in different colors. The report remains selectable and copyable.
- Corrected the previous misleading **DC WRITABLE: No / not verified** result: RootDSE does not guarantee an `isRODC` attribute. The native LDAP discovery now reads **msDS-isRODC** from the selected controller's NTDS Settings entry, resolved through RootDSE `dsServiceName`. A verified FALSE indicates a writable DC, TRUE indicates an RODC; if no trustworthy answer is available the UI explicitly says Unknown.
- Updated all native AD dependent paths (deployment preview, GPO lifecycle list/preview, computer/group assignment, DC consistency, SYSVOL storage) to use the same verified DC state. Unknown never grants write permission.
- Initial GPO ACL assessment is *read-only* and does not prove effective delegation. The selected OU, existing GPO and SYSVOL permissions are rechecked before deployment. The connection wizard never writes to AD.

## Update indicator and checker

- The main window and Campaign Wizard now **always show a Check updates control**, even when no new release is known. This also provides a visible route to the updater if an earlier background check failed.
- A verified newer GitHub release changes the control into a noticeable amber Update button including the new version; API errors show a retry warning instead of being invisible.
- Read-only checks run at startup, on window activation after the interval and every 10 minutes while the application is open.
- Clicking Check updates or the update notice opens the existing two-step Updater (Check -> Install). The manager does not download or install silently.

## CI and safety

- Windows x86/x64 compile, WinForms DPI smoke and existing offline AD/PowerShell contract tests are required before publishing this prerelease.
- The legacy PS1 administrative actions that have not yet been migrated remain guarded. This update does not claim all PS1 has been removed.
- Real DC and Windows endpoint deployment validation must be performed separately on an isolated OU; CI runners do not join the organization's AD domain.
