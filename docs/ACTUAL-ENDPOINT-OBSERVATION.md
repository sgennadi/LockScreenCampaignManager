# Actual Endpoint Lock Screen Observation — 0.4.0-preview.26

**Status:** explicit administrator-initiated, **read-only** native 64-bit remote HKLM observation for exactly one verified domain computer. This is an **endpoint value comparison**, not a cryptographically verified Group Policy winner, authentic RSoP, or proof that a JPEG appears on the lock screen. Normal GitHub-hosted CI has no real domain access.

## How to use

1. In **GPO Operations**, select the existing managed GPO and verify the OU and computer security group context.
2. Open **Lock Screen conflicts**, enter the AD computer **FQDN**, and click **Analyze effective (PC)**.
3. The report performs the existing signed/sealed LDAP/SYSVOL read-only candidate analysis on the named DC.
4. Click **Verify endpoint HKLM**, review the clear confirmation, and approve **one** read-only request. The manager **rechecks selected DC, managed campaign/GPO identity, actual AD computer in the selected OU subtree and group membership** before contacting the remote registry.
5. Click **Winning policy analysis**. Inspect the additional **Observed HKLM** and **Value comparison** columns, plus details for missing, unreadable, different, matching, and observed-only CSP settings. You may separately **Import gpresult XML** from a manually exported computer report.
6. **Clear HKLM snapshot** discards the observation held only in the dialog. The existing private CSV export requires additional explicit approval and is local only.

The observer does not automatically retry, run on all clients, open a remote shell, send data to GitHub, schedule tasks or attempt any remediation.

## Observed values

On the explicitly selected and AD-verified computer, Windows PowerShell 5.1 opens **Remote Registry, Registry64, HKLM** and attempts to read only:

- `SOFTWARE\Policies\Microsoft\Windows\Personalization` — `LockScreenImage`, `NoChangingLockScreen`, `NoLockScreen`
- `SOFTWARE\Policies\Microsoft\Windows\CloudContent` — Windows Spotlight / rotating lock screen policy fields
- `SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP` — `LockScreenImagePath`, `LockScreenImageUrl`, `LockScreenImageStatus`
- `SOFTWARE\Microsoft\Windows NT\CurrentVersion` — `CurrentBuildNumber` and `InstallationType` for an OS identity hint only

Values are bounded and the native registry type is recorded. ExpandString is read without expanding environment variables. The model rejects forged scope fields, contradictory status/values, unexpected keys, duplicate settings, missing expected item slots, stale timestamps, and unsupported OS claims.

## Interpretation and safety

| Display | What it means |
| --- | --- |
| Matches static estimate (source unverified) | The remote HKLM value matches the calculated GPO candidate value at that observation. **It does not identify the writing GPO.** |
| Differs from static estimate (source unknown) | The remote HKLM value and static priority estimate differ. The cause might be WMI/security, GPP, script, MDM, replication, or manual changes. |
| Absent (observed) | A specific value was absent from a **readable** registry key/snapshot. It does not imply GPO is unlinked or client agent absent. |
| Unreadable, Oversized, Unsupported | The particular value cannot be reliably compared, even if the overall request partly succeeded. |
| Unavailable | RemoteRegistry RPC/access was not available; **no absent settings are inferred**. |
| Windows 11 not confirmed | OS build and installation type do not sufficiently establish a Windows 11 client; comparison is withheld. |
| Observed without a ranked static GPO source | A value exists at an inspected registry path (including CSP), but the GPO scanner did not assign a static source; its writer remains unknown. |

A point-in-time value match is not the physical lock screen display, does not prove the underlying file exists and has the expected hash, and does not prove Group Policy applied a particular individual registry setting. Importing unsigned `gpresult /x` XML adds **GPO-level** evidence only and does not change this conclusion.

**The feature will never automatically call a policy a "verified winner".** A genuine per-setting authoritative source is a later, independently reviewed endpoint RSoP/CSE evidence task. The previous read-only Winning Policy Analysis, GPO Operations, Repair and deployment flows are preserved.

## Operational boundaries

- No RSAT or WinRM required. The existing Windows remote registry RPC/firewall/admin permissions must already be available.
- The tool never enables/starts RemoteRegistry, edits the firewall/registry/GPO or deploys scripts to a client.
- A denied/offline PC returns an **Unknown/Unavailable** snapshot, without treating a network failure as "none".
- The scope is verified on the **selected DC each time** before the read. No uncontrolled remote host or domain switching.
- Registry/CSP paths, internal FQDNs, GUIDs and local CSV may be confidential. There is no automatic cloud upload.
- GitHub-hosted Windows x86/x64 CI tests PowerShell parsing and non-mutating source contracts, scope/data parser rejection, matching/differing/absent/offline/partial/non-Windows-11 cases, and the UI without calling a DC.
- Real Windows 11 endpoint testing remains deferred because no domain-connected runner is available. **Stable version remains unchanged.** App, agent and updater are **0.4.0-preview.26**.
