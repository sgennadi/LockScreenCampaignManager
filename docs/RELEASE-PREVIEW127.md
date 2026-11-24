# LockScreen Campaign Manager — 0.4.0-preview.127

## What's new

1. **Readable GPO analysis.** Automatically wrap observations and next steps; keep selected-row details synchronized with English, Russian and Hebrew, including timer comparisons.
2. **Consistent Client Health reports.** Clear outdated reports and alert baselines when the SMB source or campaign changes; discard results from earlier asynchronous reads.
3. **Reliable exports.** Keep CSV and final HTML exports on the same captured report and abort when its source changes during the export dialog.
4. **Improved fleet interface.** Wrap and scroll AD, SMB and RPC statistics, automatically size Client Health headers and visible rows, and preserve status colors and readable agent details.
5. **Regression coverage.** Extend offline source-consistency and WinForms layout checks, including language switching and simulated UI scales up to 200%.

## Update

Open **Check updates**, select the **Preview** channel, review What's New and explicitly approve Install. Existing update integrity checks, rollback behavior, campaign data and manual deployment/Repair authorization are unchanged. The update itself does not modify AD, GPOs, SYSVOL or endpoints. SHA-256 integrity is not publisher authentication.

## Validation and limitations

All three component changes passed Windows x64/x86 and Offline AD Lab CI before integration. The release commit must independently pass its required build, self-test and package verification before publication. Simulated UI-scale tests are not physical Windows 10/11 monitor or RTL acceptance. Some dynamic technical observations remain in English. Client reports and TCP/135 reachability are diagnostic hints, not proof that an agent is running or that the Screen Saver is visible. The AD-only GPO analyzer still estimates policy applicability and priority rather than proving endpoint RSoP. Existing restrictions on automatic User GPO deployment of custom/JPG Screen Savers remain unchanged.
