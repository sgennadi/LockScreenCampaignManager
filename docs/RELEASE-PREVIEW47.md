# LockScreen Campaign Manager 0.4.0-preview.47

## Computer & Group Assignment migrated to native C# LDAP
- Search for computers in the explicitly selected OU and check direct, nested and primary group membership without spawning PowerShell.
- Preview a single Add/Remove operation and bind the approval hash to the exact computer GUID, group GUID, group change sequence, membership, OU, DC, action and UTC timestamp.
- Apply uses signed/sealed LDAP and changes **one group member only**, after a fresh read of the group and computer plus explicit typed computer SAM confirmation.
- Reject protected and built-in groups, primary groups, out-of-scope computer objects, stale previews, changed group USN or computer identity, and nested-only removals.
- The legacy Computer-GroupAssignment.ps1 remains as an offline reference and is no longer embedded in the manager EXE.
- No direct Group Policy or client registry changes are made by this module.

## Existing functionality
- Native C# read-only AD discovery and DC Consistency from preview.45/46.
- Wizard title/step layout improvements, two-step Check > Install updater, manually reviewed GitHub diagnostics.

## Validation and rollout
- Windows x64/x86 CI builds, offline self-tests and UI smoke tests; these do not verify membership writes against a live domain.
- Test group assignment with a **nonprivileged test security group** and a dedicated test computer in a controlled OU before using on production AD.
- GPO deployment, GPO lifecycle, conflict scanning, immediate client actions and SYSVOL storage continue to use their existing PowerShell engines. The client-side LockScreen-Agent.ps1 remains unchanged.
