# Post-Cleanup Verification — 0.4.0-preview.21

**Status:** preview, GitHub-hosted x86/x64 tests only. No changes were made to a real domain controller or Windows 11 machine. English-only interface, no RSAT, WinRM, gMSA runner or external PS1 installer needed.

## What is actually checked

The deployed client agent now writes a **separate post-cleanup receipt** only **after** its normal task/file cleanup has run. The older `Completed` SMB Client Health report is still emitted *before* final deletion and remains a progress claim, not proof. The receipt is local to the client in the protected ProgramData completion directory; it contains the campaign ID, UTC timestamp, agent's post-deletion task/file/owner observations and the expected original HKLM registry values *only if restoration was actually observed*. Old preview installations without this new receipt are **not** silently considered verified.

A new **read-only independent check** from the management computer cross-checks:

- Current AD computer name in the **selected OU AND security group**, the exact managed GPO and campaign ID (fresh selected-DC validation).
- Readable administrative SMB path and **absence** of the exact campaign's local installation folder.
- No matching active campaign owner record (an unrelated newer owner is flagged as needing review, not overwritten).
- A valid final receipt created after deletion, with matching campaign ID and explicit restoration flags. The receipt itself is **client-generated and not cryptographically signed**.
- Remote Task Scheduler (read-only COM) absence of the matching campaign, feedback and one-time LSCM tasks.
- Remote HKLM via Remote Registry (read-only) with the exact **existence, registry type and value** of LockScreenImage and NoChangingLockScreen matching the original states in the receipt, including *missing/absent* values.

No new remote tasks are scheduled and no remote PowerShell script runs. No registry modifications, GPO lifecycle changes, repair RPC, automatic retries or background monitoring are performed. Any missing privilege, network outage or inaccessible Remote Registry prevents a positive verification. The check uses the existing DomainPowerShellRunner with the user's selected credentials and the existing managed-GPO scope validation.

## How to use

1. Open **GPO Operations → Device & Repair Dashboard**, select the existing managed GPO, and choose **Load verified AD devices → Refresh reports**.
2. Open **Closure audit**. Select **1–25** computers in the grid; use Ctrl/Shift to select more than one.
3. Click **Verify selected (read-only)**. Confirm the explicit network-read prompt.
4. Inspect the **Post-cleanup check** and **Last observed** columns and the detailed reason/flags. Offline computers remain **Unknown** (never "clean"), and old agent installations without a final receipt remain **Pending**. Click the same button again only when you manually choose to recheck.
5. Reopen the window later: the last observations persist locally, scoped to the domain, DC, OU, security group, campaign ID and GPO GUID. A previously observed clean state is labelled *Last observed*, not certified current. Removing a computer from the verified OU/group does not allow its cached record to grant access.
6. Use **Export visible CSV** only after confirming; the file is stored locally, with private computer/campaign/GPO details and spreadsheet-formula escaping. No automatic GitHub upload.

## Status meanings

- **Observed clean (not a signed attestation):** all checks matched the client's after-cleanup receipt at the given timestamp.
- **Pending:** the local campaign folder, owner or scheduled tasks remain, or there is no final receipt.
- **NeedsReview:** inconsistent receipt/registry, a newer campaign owner or an unexpected finalization state.
- **Unknown:** SMB/Remote Registry/Task Scheduler unavailable, permission denied, invalid remote data or the client is offline.
- **Not checked:** no manual verification has been attempted for that AD computer in this selected scope.

Even when every PC was once observed clean, this does **not** prove current Windows RSoP, DFSR/AD replication, physical lock-screen rendering, or absence of a different policy. GPO unlink/disable and client cleanup remain separate decisions. Never delete SYSVOL/GPO solely because cached observations look healthy. Existing GPO Preview/full-GUID confirmation is unchanged.

## Privacy, persistence and failure modes

Last observations are kept per Windows user under `%LOCALAPPDATA%\LockScreenCampaignManager\PostCleanupVerification\<scope-hash>.json`. They are not uploaded and no passwords/tokens are stored. The cache is bounded, validated and atomically replaced, and rejects reparse-point files. An invalid local cache raises a visible error rather than inventing successful verification. Unreachable or offline PCs are retained until a later **explicit** manual check.

The remote check accesses administrative SMB (C$), remote Task Scheduler COM and Remote Registry. Network/firewall and administrator rights must permit those **reads**. If Remote Registry is disabled, verification is **Unknown**; no service is enabled automatically. Full real DC/Windows 11 acceptance is still deferred.

## GitHub CI coverage

Windows x86/x64 build and PowerShell 5.1 parser, hosted agent lifecycle tests (post-cleanup receipt after file/task removal), scoped/host-restricted evidence parsing, false-positive prevention, offline persistence across partial rescans, corrupt-cache rejection and English WinForms DPI/layout smoke tests. Hosted runners **do not** reach an actual DC or Windows 11 endpoint.
