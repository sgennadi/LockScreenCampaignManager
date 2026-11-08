# LockScreen Campaign Manager 0.4.0-preview.55

## Native C# GPO Deployment Plan (read-only)
- The AD/GPO Deployment **Preview / check conflicts → Plan** operation now runs directly in native C# using signed/sealed LDAP and read-only SYSVOL, without RSAT or launching powershell.exe.
- Selects an explicit writable DC; checks verified RootDSE, clock skew within five minutes, valid OU/group scope, security-group SID and direct member count. Ranged/incomplete AD group membership is rejected rather than reported as an exact count.
- Fully validates the locally exported campaign manifest, SYSTEM agent hash, image filenames and SHA-256, time zone, strictly increasing schedule and ambiguous/invalid times.
- Checks duplicate GPO display names and whether the target campaign already exists on the selected DC's SYSVOL. Missing access/SMB errors are not treated as proof that a target is free.
- Returns the existing preflight contract expected by the deployment wizard (image count/bytes, OU/group, GPO name, script path, selected DC time and diagnostics). No AD, GPO, SYSVOL or client writes occur in Plan.
- Adds isolated on-disk Windows self-tests of a valid package and a deliberately tampered JPEG.

## Unchanged guarded write paths
- **Deploy GPO + files remains on the existing PS1** with independent preflight, copy verification, tight security filtering, new GPO creation and fail-closed partial-deployment handling. Do not consider a Plan output permission to skip these guards.
- Immediate Actions **List + Preview**, GPO Lifecycle **List + Preview**, SYSVOL **Scan + Preview**, group membership and base GPO conflicts remain native C#.
- Immediate remote execution, GPO Lifecycle Apply, SYSVOL deletion and full Effective GPO analysis retain their previous engines. Windows client LockScreen-Agent.ps1 is unchanged.

## UI from preview.54
- Full six-step Campaign Wizard, DPI-safe headers and non-blocking Update available indicator with direct Updater transition are retained.
- Automatic update checks never download/install; updater keeps explicit Check and Install.

## Validation
CI validates Windows x64/x86, GPO and package safety tests, local SHA-256 tamper rejection, and multi-scale WinForms layouts. A live AD/DFSR/gpedit.dll test remains required before any production Deploy operation.
