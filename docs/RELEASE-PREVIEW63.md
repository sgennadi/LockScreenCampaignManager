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

## Campaign Wizard image preview

- **Images** and **Arrange** now render the selected bitmap inside the Wizard itself, alongside the file list; Advanced is not opened behind the Wizard.
- The 100–200% DPI layout reserves actual space for the bitmap instead of allowing headers and buttons to collapse the preview.
- A dark preview frame, missing-image placeholder, file name, dimensions and image-loading errors remain visible inside the current page.
- A Windows UI regression test now creates a real temporary PNG and checks that the selected image is decoded and the rendered preview has nonzero usable dimensions at the smallest supported window size.

## Native administrative C# migration

- Administrative AD inventory, permission hints, computer/group assignment, GPO deployment, GPO lifecycle including reuse/delete, SYSVOL storage/cleanup, immediate remote actions and GPO effective/conflict/evidence inspection all route to native C#.
- GPO write operations use LDAP/Windows GPO COM, remote one-time actions use Task Scheduler COM/RPC, endpoint snapshots use native Registry64, RSoP DCOM and read-only SMB hashes.
- The old administrative PowerShell runner and its script resources are removed from the EXE. Legacy PS1 source files may remain in GitHub as **test fixtures or reference implementations** but are not packaged.
- **The client-side LockScreen-Agent.ps1 remains** because it is the separate endpoint worker deployed by the campaign's GPO. Converting the client agent to a signed EXE is a distinct migration with per-client installation/rollback requirements.

## CI and safety

- Windows x86/x64 compile, WinForms DPI smoke and existing offline AD/PowerShell contract tests are required before publishing this prerelease.
- All administrative entrypoints in the manager are routed through native C#. The separate client agent is still PowerShell; Windows lab tests do not prove that every domain write, GPO filter, remote RPC or visual lock screen behavior works on real client machines.
- Real DC and Windows endpoint deployment validation must be performed separately on an isolated OU; CI runners do not join the organization's AD domain.
