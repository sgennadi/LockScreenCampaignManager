# Smart SYSVOL Storage — 0.4.0-preview.16

This is a controlled **preview**. Use a dedicated test OU, test GPO and test campaign before allowing cleanup in production.

## Where source files are stored

On the selected DC:

`\\<dc>\SYSVOL\<domain>\scripts\LockScreenCampaigns\<campaign-id>\`

Every campaign keeps its own immutable `campaign.json`, `LockScreen-Agent.ps1`, and `images/01.jpg...`. There are **no hardlinks, symlinks or shared source folders**. The existing deployment process still refuses to overwrite the same Campaign ID and verifies SHA-256 before making a new GPO.

The managed GPO itself lives under the usual `\\<dc>\SYSVOL\<domain>\Policies\{GUID}`.

## New GUI

Open **AD / GPO Deploy → GPO Management / Conflicts / Push Now → Smart SYSVOL Storage**.

- **Scan SYSVOL** — read-only audit of up to 500 campaign folders on the chosen DC, comparing actual image SHA-256 with manifests and counting duplicates across campaigns. Shows campaign ID, end time, image count, duplicate image count, matching GPO count and cleanup eligibility.
- **Compare current campaign** — builds a temporary local export, checks which normalized JPEGs are already present in verified SYSVOL campaign folders, then removes the local staging directory. Does not skip uploads or modify the DC.
- **Preview cleanup** — revalidates the exact campaign directory, all images, hash fingerprints, campaign end time and domain GPO references; never modifies files.
- **Delete source folder** — disabled until a fresh Preview succeeds and the operator types the exact Campaign ID. Cleanup is scoped to that single source directory; it does not delete GPOs, OU links, registry values or client tasks. No background cleanup.

New campaign deployment **Preview (no changes)** also attempts a read-only duplicate comparison and reports the result. If that optional audit is unavailable, it explicitly says so rather than claiming zero duplicates.

## Safety rules

1. The chosen AD domain and DC must be verified by signed/sealed LDAP. For deletion, the selected DC must confirm it is writable.
2. The selected DC's `RootDSE.currentTime` provides the reference **UTC** clock; the manager shows this time and the workstation-to-DC difference. A skew above **5 minutes** blocks GPO deployment, GPO Reuse and SYSVOL cleanup. Display hours are interpreted using the campaign's chosen Windows time zone (for example `Israel Standard Time`).
3. The campaign must have completed **at least 30 days earlier** (measured by the DC). This is a grace period, not evidence that every offline computer finished cleanup.
4. The full domain GPO reference audit must succeed. Incomplete replication, missing GPT folders, inaccessible files, an unexpected structure or unrecognized data fails closed. A partial audit may still show duplicates, but must not enable deletion.
5. No GPO may reference this campaign source. Existing GPOs may be disabled or unlinked and still reference the source; these also block cleanup. Admins must remove or reuse the GPO first, after inspecting links on all relevant OUs and sites.
6. Every expected file must be present and match its SHA-256; extra files, unknown names, symlinks and reparse points block deletion.
7. The cleanup approval is tied to source fingerprint, selected domain/DC, campaign ID and five-minute UTC preview. The engine rechecks the current folder and GPOs immediately before attempting removal.
8. Before cleanup, inspect client health (including offline PCs), deployment history, DFS Replication, and any manually created script references. No automated scan can prove there is no external consumer of a SYSVOL script.

**Caution:** AD, SYSVOL and DFSR do not provide a single atomic deletion transaction. The guarded removal is intentionally an admin-only pilot feature; it does not guarantee that every offline client has received cleanup.

## Time synchronization

Windows clients continue to follow their domain's Windows Time hierarchy. The agent changes images according to its locally synchronized Windows clock, allowing scheduled changes while SYSVOL/DC is temporarily unavailable; it does not poll a DC for every change. Verify `w32tm /query /status` and `w32tm /query /source` on representative pilot clients. A time comparison at deployment cannot guarantee the clock of every client at all later times.

## Release status

Windows CI must pass x86 and x64 PowerShell 5.1 parsing, offline clock and cleanup guard regression, C# self-test, WinForms DPI smoke and ZIP packaging. CI never connects to a production DC or actually deletes SYSVOL folders. Live AD + GPMC and interactive 200% DPI tests remain mandatory before stable.
