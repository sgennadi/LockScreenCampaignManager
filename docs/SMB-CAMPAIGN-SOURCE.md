# LockScreen Campaign Manager — campaign files in SYSVOL or SMB

**Preview.89 validation extension:** a disposable Windows 10/11 endpoint can use the [LocalSystem SMB canary](SMB-SYSTEM-CANARY.md) with a *new deployment receipt* to check computer-account SMB read access and exact SHA-256 data. This is additional evidence; a real reboot, gpresult and restore test are still needed.

## Choice in the deployment Wizard

In the **main Campaign Wizard → Step 6 Save / Skip** (before saving the project) or **Step 7 Deploy**, select **Campaign files**. The same setting is available in **AD / GPO Deployment → Step 2 / 3**:

- **SYSVOL (default)** — keeps the prior behavior. The immutable campaign folder lives under the selected domain's SYSVOL campaign scripts directory.
- **SMB network folder (UNC)** — choose or type an existing dedicated folder such as `\\files.example.com\Campaigns`. The Manager publishes one new, non-overwriting `<campaign-id>` subfolder containing `campaign.json`, images and the exact pinned `LockScreen-Agent.ps1` or `.exe`.

The setting persists in the editable campaign project; old projects still use SYSVOL. The **Client / GPO method** choice is independent: PowerShell uses PowerShell Startup Scripts; the EXE remains restricted to approved isolated lab canaries until its separate production readiness gate is met. Group Assignments creates separate unique package directories for each Lock Screen group.

**Important:** An Active Directory GPO's policy metadata and Startup scripts registration cannot be moved out of SYSVOL. In SMB mode, only the *campaign payload* is stored outside SYSVOL. GPO Startup references the approved UNC agent path. No changes are made to unrelated GPOs. Built-in Windows Screen Saver user policies do not need a JPEG package; automatic **JPG Photo Screen Saver User GPO deployment remains disabled**.

## Prepare SMB safely

1. Create a dedicated SMB share on an organization-controlled server before Preview. Use an ordinary UNC folder, not SYSVOL, NETLOGON, an administrative share, a mapped drive, WebDAV, or a symlink/junction/DFS reparse-point target.
2. On **both NTFS and SMB share permissions**, allow the intended **Domain Computers** or tightly scoped test computer accounts to **Read & Execute**. Limit package creation/change/delete rights to authorized administrators/service identities only. Do not grant Users, Authenticated Users, Everyone, Domain Users or Domain Computers write permission. Restrict server administrators and audit modifications.
3. Verify name resolution, Kerberos or approved domain SMB authentication, firewall TCP/445, startup network availability, effective computer-account access and consistent DFS target content from the *actual* pilot client. Access from the Manager's logged-in admin alone proves none of these. Avoid unsigned binaries on untrusted shares; enforce relevant AppLocker/WDAC policy.
4. Enter the full **existing** UNC parent folder and click **Preview / check conflicts**. The read-only preflight checks path syntax, disallows SYSVOL/NETLOGON and reserved shares, inspects visible non-reparse NTFS ACL ancestry for broad **Allow-ACE write permissions**, validates immutable local campaign files, checks for source collision and scans GPO scope/conflicts. This is an **advisory NTFS scan only**, not an effective ACL evaluation. The Preview explicitly reports **SMB share ACL NOT TESTED**, **computer-account READ NOT TESTED** and **startup access NOT TESTED**. It does not create files or test permissions by writing. The optional Smart SYSVOL image-duplicate scan is **skipped in SMB mode**, because SYSVOL does not host the selected payload.
5. Review the source path in Preview and explicitly confirm deployment. The Manager re-runs AD/SMB checks, stages and hashes the package on the same network share, atomically renames it to the campaign ID, and only then creates and links the new GPO. Failure can leave staged or unlinked content for administrator investigation; it never replaces a prior campaign folder.
6. Test **one** domain-joined Windows 10/11 Enterprise/Education computer first: run `gpresult /scope computer /r`, inspect Startup script execution logs, verify the local cached images/agent hashes and client health, and reboot once with the normal startup network policy. Check actual screen behavior. Only then expand the computer group.

## Expected behavior and operational limits

| Item | SYSVOL | SMB share |
| --- | --- | --- |
| GPO creation/link and security filtering | AD/SYSVOL | AD/SYSVOL |
| GPO GPT / startup settings | Selected DC SYSVOL | Selected DC SYSVOL |
| Immutable campaign files | Domain SYSVOL scripts folder | Dedicated SMB UNC campaign folder |
| Client Startup command | SYSVOL UNC path | SMB UNC path |
| Images after successful installation | Verified local endpoint copy | Verified local endpoint copy |
| Per-device reporting | Independently configured Client Health SMB share | Same independent Client Health configuration |
| Source cleanup | Existing SYSVOL lifecycle tools | **Manual**, after GPO unlink and verified endpoint cleanup |

### Critical caveats

- **A green Preview is not proof of machine-account access.** Its NTFS broad-write Allow-ACE scan is **advisory** and cannot verify effective NTFS permissions, SMB share ACLs, Kerberos, cross-domain trusts, offline network startup or user/computer RSoP. Check actual `LocalSystem` SMB access on an isolated approved test client.
- **Do not delete source files immediately after unlinking a GPO.** Confirm endpoints have applied cleanup and no future startup/repair operation depends on the source. External SMB folders are not automatically managed by SYSVOL cleanup tools in this preview.
- Network file availability and protection at execution time matter: Windows executes the PS1/EXE from the UNC path at Startup before the agent verifies and installs the local campaign. Only use a trusted, access-controlled server; never allow broad write permissions.
- Never put domain login passwords in a campaign project, manifest, folder name, script or report. The Manager does not write credentials into the published content.
- The new choice is for **Lock Screen campaign files**. The separate Preview.82 JPG `.scr` manual Computer GPO lab kit is unchanged and still needs isolated endpoint and User GPO applicability tests.

## Pilot checklist

Record the selected storage mode, server/share (privately), client OS, intended computer group and OU, SHA-256 of the source manifest and agent, and the applicable GPO GUID. Check both a successful Startup and failure behavior (share inaccessible, file hash mismatch, incorrect permissions) on a disposable test PC. Review `gpresult /h` locally without uploading private AD data. Test GPO unlink, client cleanup, and later source deletion in that order.
