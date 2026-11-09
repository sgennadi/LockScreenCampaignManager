# LockScreen Campaign Manager 0.4.0-preview.64

## Deployment Wizard: readable OU/group selection and nonblocking preview

- The **GPO rights** check is a strictly read-only advisory. A result such as NO_MATCHING_ALLOW, UNKNOWN or POSSIBLE_DENY no longer opens a blocking Yes/No confirmation merely to preview a campaign.
- Connection and permissions are shown independently, with color-coded status. Unknown/incomplete tokenGroups, possible denies and missing matching allows are not misrepresented as proof of effective permissions.
- The full-width Targets page shows short OU and group names; full distinguished names remain searchable and appear in hover tooltips and the final deployment preview.
- The Connection page is distinct from Targets and Preview. Targets and Preview use the height previously occupied by already verified connection fields. Advanced mode retains the combined editor.
- GPO deployment still requires an explicit write confirmation and fresh native AD/COM/SYSVOL checks. Preview never grants AD rights.
- Added offline contract regression assertions and an in-process Windows x86/x64, multi-scale WinForms smoke window for Deployment Wizard.

## What is not included

- Manager administrative operations remain native C#. The deployed endpoint client is **still LockScreen-Agent.ps1**; no native EXE client is shipped in this release. Selecting PS1 vs EXE delivery will be implemented only when the EXE agent and its GPO startup/upgrade/rollback path are feature-complete and tested.
- GitHub CI cannot prove effective GPO delegation, SYSVOL replication or Windows lock-screen behavior on a live domain. Validate against a disposable OU and limited client group before production.
