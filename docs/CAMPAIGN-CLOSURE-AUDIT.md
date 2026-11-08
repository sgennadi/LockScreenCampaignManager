# Campaign Closure Audit — 0.4.0-preview.20

This preview is a **read-only closure assessment**. It is **not** a full automated shutdown feature and does not prove local task deletion, registry restoration or physical lockscreen rendering. A real domain pilot is intentionally postponed.

## Where to find it

Open **GPO Operations → Device & Repair Dashboard**, choose a managed GPO, click **Load verified AD devices**, then **Refresh reports**. Click the new **Closure audit** button. The window is in English, DPI-aware and remembers its size/position.

The review uses the verified OU + security-group computer intersection, the latest Client Health feedback, and both User and protected gMSA Repair queues. If the snapshot is more than 10 minutes old, refresh first. No test, GPO modification or remote RPC is initiated by the audit.

## Two separate evidence layers

**AD selected-DC metadata** is read fresh through the existing no-RSAT GPO Lifecycle List operation. The audit shows direct link state on the selected OU, GPO computer-configuration state, and number of other OU/domain/site links. An absent/disabled *direct* OU link is **not** proof that the policy is not inherited or applied by other means. Site links, WMI filters, security ACL, replication between DCs, endpoint RSoP and existing scheduled tasks remain independent. On an AD read failure, the displayed GPO state is **Unknown**, not "unlinked".

**Endpoint reports** are self-reported SMB JSON, never an independent attestation. The current agent emits Completed **before** its final local file/task deletion. The category **Reported completion** therefore does not mean independently verified registry restoration or task removal. The FleetShutdownIndependentlyVerified property is intentionally always false until there is a separate safe, post-cleanup endpoint verification mechanism.

## Per-computer classifications

- **Repair unresolved**: User/gMSA Repair remains tracked, including an uncertain RPC outcome; it takes priority over an agent Completed claim.
- **No report**: no accepted client feedback; possibly offline, not automatically broken or finished.
- **Reported completion**: Completed claim only; not a cleanup guarantee.
- **Needs review**: stale, failed or inconsistent Client Health.
- **Active or pending**: agent is still running or waiting; GPO Disable/Unlink will not remove its existing task.
- **Unclassified**: unexpected state, which is never presented as success.

The totals cover only the verified AD computer intersection, up to 5,000 machines. A stale/empty/mismatched snapshot is rejected rather than giving false percentages. The audit contains no mass cleanup controls; any manual Cleanup still goes through the existing separate Preview and full-GPO-GUID confirmation workflow.

## GUI and evidence export

The dialog includes filters for all categories, search by computer, per-device explanation and next recommended action. **Export visible CSV** requires explicit confirmation and saves only to a selected local path. It warns that computer names, campaign IDs and GPO GUIDs are sensitive. CSV fields are escaped and spreadsheet formulas are neutralized. No automatic upload to GitHub or diagnostics module is performed.

## Limitations / future extension

Real completion verification requires a post-cleanup read of selected Windows 11 clients under a safely delegated identity, checking the absence of campaign tasks/files and the restoration of the original HKLM lockscreen state. This must respect computers that are offline, conflicting policies, previously missing values and asynchronous AD replication. It is **not** implemented by this read-only preview. A green x86/x64 GitHub-hosted CI build is not a DC or Windows 11 acceptance test.
