# RSoP Registry CSE + Image Integrity — 0.4.0-preview.27

This stage adds **two optional, independently scoped read-only observations** on top of preview.26.

**Important:** A cached computer RSoP record with `precedence = 1` identifies the reported winning Registry Policy CSE source for **one registry policy setting**. It does not prove that the setting is still applied, that the campaign agent wrote it, or that a JPEG appears on a lock screen. A matched file hash only proves that the inspected file matched the selected DC's manifest at the time of the read.

## UI

From **GPO Operations → Lock Screen conflicts → Analyze effective (PC)**, select the existing managed campaign GPO, the verified AD computer FQDN, then click **Verify RSoP + image**. After explicit approval, the manager:

1. Rechecks the managed campaign/GPO and **actual computer membership in selected OU + security group** on the selected DC, just as with Verify endpoint HKLM.
2. Reads the native 64-bit Lock Screen registry values through **Remote Registry**.
3. Queries existing **root/RSOP/Computer — RSOP_RegistryPolicySetting** through read-only remote **WMI/DCOM**. Only narrowly listed machine Lock Screen/Spotlight names and supported policy keys are kept. No gpupdate or new RSoP session is generated.
4. Checks whether the current LockScreenImage string points to **C:\ProgramData\LockScreenCampaigns\<campaign>\images\NN.jpg**. Other drives, external paths, non-campaign images and ambiguous filenames are **not** accessed.
5. Reads the matching expected SHA-256 from the selected DC's existing **campaign.json** and hashes only the corresponding already-existing file under **\\computer\C$\ProgramData\LockScreenCampaigns\<campaign>\images\NN.jpg**. Paths with reparse points, missing files and oversize images are rejected or reported unknown.
6. Displays the **cached CSE GPO GUID** for each applicable registry setting alongside the existing static precedence and observed HKLM value; displays **JPEG HashMatches / HashDiffers / Unknown** separately.

There is a privacy prompt before performing the read. **Clear HKLM snapshot** also clears the corresponding RSoP/file evidence, avoiding a mixed-age analysis. A later Verify endpoint HKLM replaces any earlier source evidence; it is never silently combined.

## Status interpretation

| Item | Meaning |
| --- | --- |
| CSE reported winner (cached) | One non-deleted Registry Policy CSE row with precedence=1 and an unambiguous GPO GUID was returned by remote WMI. |
| Ambiguous / Unknown GPO ID / Deleted setting | No unique attributive source is reported, or the action is deletion. |
| Empty / Unavailable / Skipped | No CSE source conclusion is possible. Does **not** prove GPO absence. |
| HashMatches | SHA-256 of the narrowly mapped remote JPEG equals a matching entry in campaign.json, retrieved from the selected DC. |
| HashDiffers | Expected and observed SHA-256 disagree. The file may have been replaced or damaged. |
| FileUnavailable / ManifestUnavailable / OtherOrUnsupportedPath | Unable to verify the file; never report missing as a successful match. |

## Safety and limitations

- **No RSAT, WinRM, remote script execution, task creation, service activation or registry edits.** Existing Remote Registry, remote WMI/DCOM and SMB permissions/firewall configuration must already allow read access.
- Fixed computer from selected DC; no UNC/host selection by raw registry string; no arbitrary file hashing, following reparse points, writes, hidden repair or automatic retries.
- The cached RSoP source only represents the **Registry Policy CSE**. GPP Registry Preferences, startup scripts and MDM/CSP settings can modify registry values independently. Presence of a CSE source does not establish it as the current writer.
- Reading a physical JPEG through SMB and comparing its hash to the SYSVOL manifest **does not prove it is displayed**. This requires separately authorized, interactive Windows 11 testing, not GitHub-hosted CI.
- Date/time is a read observation time, not proof of the last GPO refresh. Failure to read WMI/SMB is marked Unknown, never interpreted as an absent GPO or displayed image.
- The opt-in CSV export can reveal private computer names, policy GUIDs and hash values, and is never uploaded automatically.
- GitHub Actions runs synthetic parser, security-contract and WinForms/DPI tests for x86/x64. **No real Windows 11/DC/gMSA acceptance** occurs in hosted Actions. Stable remains untouched.

## References

- [Microsoft RSOP_RegistryPolicySetting](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/policy/rsop-registrypolicysetting)
- [Microsoft RSOP_PolicySetting precedence and GPOID](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/policy/rsop-policysetting)
