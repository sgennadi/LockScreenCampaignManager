# LockScreen Campaign Manager 0.4.0-preview.41

## Clear user-facing language
- The update window is now **Software updates** instead of displaying the internal UniversalUpdater module name. All update buttons, verification and manual install/rollback behavior are retained.
- Diagnostics windows use **Diagnostics**, **Error trends** and **Report an error** instead of exposing the internal UniversalDiagnostics module name. The JSON schema and diagnostic engine remain unchanged.
- Diagnostics now includes a discoverable **Report an error...** action for an explicitly selected saved report, as well as the error popup's reporting action.
- The report action opens a GitHub Issue page and copies the user-reviewed sanitized report to the clipboard. The user must review, paste and click Submit. **No report is uploaded automatically.**
- Since the GitHub repository is public, the dialog warns against publishing private domains, users, internal machine names, file paths or secrets.
- GitHub Actions can review **already published** Issues, group diagnostic fingerprints and generate a limited aggregate artifact. It does not send reports on behalf of the application.
- Adjusted the Diagnostics layout to allow visible, DPI-safe two-row action buttons.

## Previously added features
Includes the improved visible six-step Wizard, readable AD connection controls, Windows 10/11 support, conditional GPO rights checks and operator-approved portable EXE updater.

## Validation limits
Hosted Windows x64/x86 builds and synthetic WinForms/AD tests validate the build. Real desktop DPI layout and domain-connected behavior still require a pilot.
