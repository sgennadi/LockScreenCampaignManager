# LockScreen Campaign Manager 0.4.0-preview.51

## Native C# managed GPO lifecycle inventory (read-only List)
- The GPO Operations **Refresh / List** workflow now uses signed/sealed Windows LDAP and selected-domain-controller SYSVOL file reads directly from C#, without launching powershell.exe or requiring RSAT.
- Enumerates AD GPO containers, OU/domain and site-level GPO links, preserving enabled/enforced link flags. Displays only policies whose managed startup script and campaign manifest match the application contract.
- Rejects inconsistent managed manifests, reparse points, malformed link attributes, oversized/unreadable policy files and unbounded directory inventories, rather than claiming a clean list.
- Supports current or explicitly provided AD network credentials for LDAP and SYSVOL access. Read-only queries are bounded by count and time limits.
- Includes offline self-tests for GPO GUID scoping, DC identity, link parsing and rejection of foreign or additional startup script references.

## What remains unchanged
- Lifecycle **Preview and Apply** stay on the same PowerShell engine with their shared approval hash. Disable, Enable, Unlink, Relink, Delete and Reuse continue to require explicit confirmation.
- SYSVOL Cleanup (actual deletion), GPO Deployment, Immediate Actions and extended Effective GPO verification remain PowerShell-backed. Windows client LockScreen-Agent.ps1 is unchanged.
- Native SYSVOL Scan/Preview, AD discovery, DC consistency, computer/group membership and basic GPO Conflict Scan are retained.

## Validation
Windows hosted CI x64/x86 builds and self-tests are not a live AD/DFSR pilot. Verify the managed GPO inventory against an existing test OU and domain before administering production policies. No AD or GPO writes were added by this preview.
