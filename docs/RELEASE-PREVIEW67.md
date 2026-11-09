# LockScreen Campaign Manager 0.4.0-preview.67

## AD scope and client report coverage

**Agent Management / Live** now displays three separate figures:
1. **AD OU computers**: all computer objects below the administrator-selected OU (including nested OUs and disabled devices), up to a bounded 5,000-object inventory. This is NOT a list of all GPO targets.
2. **OU + computer security group**: computers confirmed to be in the OU and nested campaign group, with valid DNS names. These are the eligible remote-action candidates (up to 2,000).
3. **Sent a valid SMB report**: eligible computers with a validated `COMPUTERNAME.json` file for the selected managed GPO campaign. Compare with the eligible count, not blindly with all OU computers. Both recent and old reports are counted separately.

The overview shows disabled/inactive AD counts, recent reports, missing reports and invalid report files. If the feedback share is unavailable, report counts show **? / UNKNOWN** instead of incorrectly showing zero. The overview does not read an arbitrary directory or use stale SMB JSON as remote-action authorization.

## AD activity filter

By default, the console shows **Active <= 90 days** based on **replicated AD lastLogonTimestamp** and excludes disabled AD computer objects. Available filters include All eligible, last 30 / 60 / 90 / 180 / 365 days, older than 90 days, Never/unknown, and Disabled. AD lastLogonTimestamp is approximate and often lags actual authentication by days; it is NOT an online/offline probe. Last activity, disabled state, RPC TCP/135 reachability, agent-reported health and task submission are distinct signals. Filtering only changes the visible list, never the GPO scope or AD membership.

## SMB configuration is discoverable in the Wizard

The guided **Campaign Wizard → Schedule** page now exposes **Client Health / Feedback...** to enable/disable reports, set the dedicated SMB share and choose a 5–60 minute reporting interval. An independent summary row shows the selected share and interval, or explicitly warns that reports are disabled. Company-level defaults continue to populate new campaigns.

Each endpoint updates its own file under the campaign directory:
```text
\\\\fileserver\\LockScreenReports$\\<campaign-id>\\PC001.json
```
A client report includes computer name, campaign ID, UTC report timestamp, agent version, stage, health and image/registry/task/file status. The SMB reader rejects mismatched identities and malformed reports; however, reports are **untrusted diagnostic hints**, not cryptographic attestation. Administrators must configure restrictive SMB/NTFS ACLs so computer accounts cannot overwrite each other's files.

## Operator controls and limitations

Agent Management continues to support **Push / Repair / Report / Cleanup** with Preview, exact GPO GUID confirmation and fresh AD/SYSVOL scope validation. A task successfully registered is not proof of visible lock screen changes. Native EXE endpoint deployment is lab-only in PR #3, not released to production. GitHub-hosted Actions validate code, offline synthetic reports and WinForms layout, not any real OU, DC or endpoint.
