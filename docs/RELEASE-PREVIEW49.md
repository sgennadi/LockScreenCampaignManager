# LockScreen Campaign Manager 0.4.0-preview.49

## Native Smart SYSVOL Storage — read-only Scan
- Replaces the Manager's PowerShell-based **Scan** mode with native C# signed/sealed LDAP and selected-DC SMB reads. No local PS1 or PowerShell process is launched for this mode.
- Inventories existing campaign manifests, agents and JPEG image SHA-256 hashes with bounded file sizes, directory counts and strict path/reparse validation.
- Compares current campaign JPEGs against independently stored copies and shows duplicate file counts and total bytes (report-only; no deduplication links or writes).
- Inspects every verified GPO SYSVOL tree for references to managed campaign folders. Incomplete, inaccessible, oversized or timed-out GPO reference audits set `gpoAuditComplete=false` and prevent cleanup-eligible hints.
- Keeps selected DC UTC time as the campaign expiration reference; cleanup hints require valid files, no linked GPO reference and a 30-day post-end grace period.
- Supports alternate AD credentials for LDAP and read-only SMB using Windows network-only impersonation.
- Includes offline regression checks for safe campaign identifiers, expiry, duplicate counts, multi-encoding campaign reference extraction and file-size rejection.

## Still PowerShell-backed
- **SYSVOL Preview and Cleanup** remain on their existing guarded PowerShell engine and must re-read all GPO references and source fingerprints before any deletion. Legacy SYSVOL resource stays embedded.
- **Conflict Scan Effective / VerifyEndpoint / VerifyEvidence**, GPO deployment, lifecycle, and immediate client operations continue to use the existing audited engines.
- Client LockScreen-Agent.ps1 is unchanged.

## Validation
CI validates Windows x64/x86 EXE packaging, self-tests and synthetic UI, but cannot replace a live AD/DFSR pilot. Start with read-only Scan on a test OU/DC and compare results with the previous version before any cleanup.
