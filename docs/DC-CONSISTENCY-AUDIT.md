# DC Consistency Audit — 0.4.0-preview.22

**Status:** read-only preview. The GitHub-hosted build can validate code, simulated UI and synthetic drift/hash comparison. It **does not** test your actual DCs or Windows 11 clients.

## GUI

In **GPO Operations**, choose the managed LockScreen GPO in **GPO: Disable / Reuse / Delete**, then open **DC Consistency**.

1. **Discover DCs** — uses the selected domain's existing signed/sealed LDAP discovery mechanism and confirms the selected baseline DC occurs in the returned AD list.
2. Select **1–12** named DCs, keeping the selected baseline DC checked. The baseline appears first. When more than 12 controllers are discovered, the result is marked **Partial scope** until another manually selected batch covers the remainder. Each audit is a separate point-in-time comparison; no cumulative “all synchronized” status is invented.
3. **Compare selected DCs** — reads each named DC and its own SYSVOL/AD state, without changing the target. Select a row to inspect timestamps, version numbers and SHA-256.
4. **Export safe CSV** — optional, locally saved after an explicit private-data warning. Hostnames, campaign IDs, GPO GUIDs and file hashes are operational data; review before sharing.

The window is English-only, follows the existing light/DPI theme, supports variable fonts and has no RSAT/WinRM dependency.

## What is checked

- **Time:** RootDSE `currentTime` from every selected DC (UTC). The manager records the local UTC midpoint of the LDAP request to estimate offsets and distinguish 120-second inter-DC differences and over-300-second absolute offsets. It never trusts the client-local time instead of the selected DC. Network delays can add uncertainty; a sample taking more than 30 seconds is considered invalid.
- **Identity:** each DC RootDSE must report the expected domain and exact DC DNS hostname; the manager also validates the discovered list and refuses cross-domain hosts.
- **Campaign package on each DC:** `\\<dc>\SYSVOL\<domain>\scripts\LockScreenCampaigns\<campaign>\`. Compare byte-level SHA-256 of `campaign.json` and `LockScreen-Agent.ps1`, and each numbered `images\NN.jpg`. Check exact image count, supported filename pattern and each image against its **own** manifest's SHA-256 (1–30 images). Reparse points, extra folders/files, unsafe names, unexpectedly sized files and invalid manifest versions fail closed.
- **GPO on the SAME DC:** `\\<dc>\SYSVOL\<domain>\Policies\{GPO-GUID}\Machine\Scripts\psscripts.ini`, `gpt.ini` and LDAP GPO `versionNumber`. Confirm that the Startup script refers to the selected campaign with `-Mode Install`, then compare the AD/GPT versions.
- **Cross-DC comparison:** match the selected baseline's manifest/agent/startup/GPT hashes, per-image names/hashes/sizes, GPO versions and clock offset. Missing or invalid evidence is **not** counted as a match.

Results include **Matching**, **Mismatch**, **Clock drift**, **Invalid**, **Unavailable** and **Unverified**, plus aggregate **Matching at scan time**, **Partial scope**, **Single DC only**, **Incomplete** or **Unverified**. Results are from the moment of scanning only. A value marked Matching does not guarantee DFS Replication convergence or client Group Policy application.

## Safety and limitations

This scan sends **no changes to AD, GPO, SYSVOL or remote endpoints**. It uses Windows PowerShell 5.1, .NET DirectoryEntry (LDAP with signing/sealing), SMB file reads and SHA-256. It neither runs remote PowerShell nor schedules tasks. Credentials are handled by the existing domain process and never exported in the CSV.

The selected managed GPO must exist on each checked DC for a complete comparison. A replica that has not received the object/files is reported as **Invalid** or **Unavailable**, not created by the application. GPOs unlinked, permanently deleted or reused into a new campaign may prevent a matching scan; investigate rather than forcing synchronization.

Results show **DC-side data only**. They do not check actual DFSR backlog, `dfsrdiag`, NTDS replication health, RSoP, computer-specific security/WMI filtering, background policy processing, or the image physically appearing on the lock screen. These require separate diagnostics or a real lab.

## CI

Windows Actions x86/x64 parse the production PowerShell script, inspect read-only command contracts, test UTC/hostname/SHA256 helpers, compile the new tab, run C# pure comparison cases (matching, mismatched hashes, drift, unavailable/invalid baseline, single-DC and partial-scope), and perform WinForms DPI smoke. GitHub-hosted CI cannot validate a real domain without authorized local execution.

**No real DC/Win11 test is performed in this preview.** Stable release remains unchanged.
