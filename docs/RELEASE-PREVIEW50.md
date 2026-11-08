# LockScreen Campaign Manager 0.4.0-preview.50

## Native SYSVOL Cleanup Preview (read-only)
- Moves Smart SYSVOL Storage **Preview** to native C# signed/sealed LDAP and selected-DC SMB, in addition to the native Scan from preview.49.
- Reads the entire domain GPO inventory, rechecks the exact campaign manifest, agent, JPEG SHA-256 hashes and unchanged source fingerprint before approving a cleanup preview.
- Requires a verified writable DC, local-to-DC UTC clock difference at most five minutes, no in-progress staging directories, a completed 30-day post-campaign grace period and no GPO that references the campaign.
- Returns a 5-minute preview plan hash in the exact format expected by the existing guarded Cleanup implementation; the selected DC identity and all source evidence are re-checked again before any deletion.
- Adds offline self-tests for the legacy-compatible SHA-256 digest and each fail-closed rule.

## Still PowerShell-backed
- **Cleanup is unchanged**: it requires explicit typed confirmation and independently re-reads every GPO and campaign fingerprint before deleting a single approved SYSVOL source folder. The script remains embedded for this reason.
- GPO deployment/lifecycle, immediate client actions and Effective/VerifyEndpoint/VerifyEvidence conflict checks still use their existing PowerShell engines.
- The installed client LockScreen-Agent.ps1 is not changed.

## Validation and limits
CI tests Windows x64/x86 packaging, pure preview hash/guard behavior, WinForms UI and existing PowerShell cleanup safety contracts. A real AD/DFSR test remains necessary before using cleanup with production data. Do not treat a read-only preview as authorization to delete archived campaigns without checking offline clients and DFSR replication.
