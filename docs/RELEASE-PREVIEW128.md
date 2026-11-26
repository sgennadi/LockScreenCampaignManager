# LockScreen Campaign Manager — 0.4.0-preview.128

## What's new

1. **Consistent Agent Management reports.** Editing the SMB root or encountering an unavailable source invalidates pending reads and clears old report counts, timestamps and client-method comparisons without losing independent RPC and task-submission evidence.
2. **Accurate coverage statistics.** Distinguish unknown AD/SMB counts from a verified empty scope and show the reason for unavailable reports.
3. **Localized fleet details.** Render Agent Management computer lists, selected-computer details, client status and RPC progress/results in English, Russian and Hebrew.
4. **Localized Client Health cells.** Translate displayed condition, stage, file counts and task/registry states while keeping canonical report values, row identities and export data unchanged.
5. **Regression coverage.** Verify language switching, evidence identity, selection preservation, stale-source invalidation and count semantics in offline WinForms tests.

## Update

Open **Check updates**, choose the **Preview** channel, review What's New and explicitly approve Install. Existing update integrity checks, campaign data, manual remote-task confirmation and deployment safety gates remain unchanged. SHA-256 integrity is not publisher authentication.

## Validation and limitations

PR #83 passed Windows x64/x86 and Offline AD Lab CI before integration. The release commit must independently pass the required build, self-tests and package verification before publication. RPC TCP/135 reachability does not establish agent health or Task Scheduler access; task submission does not establish completion. Client-reported status is unauthenticated diagnostic evidence. Technical identities, free-form diagnostic notes and exports remain canonical. Other dynamic messages remain partly English. Physical Windows 10/11, 200% DPI/RTL and actual AD deployment acceptance remain open. Automatic User GPO deployment of custom/JPG Screen Savers remains gated.
