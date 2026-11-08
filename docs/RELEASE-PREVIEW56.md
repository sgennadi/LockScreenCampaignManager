# LockScreen Campaign Manager 0.4.0-preview.56

## Native C# read-only VerifyCleanup
- **Immediate Actions → VerifyCleanup** now uses signed/sealed LDAP for scoped AD computer eligibility and a native Windows C# evidence checker for each explicitly selected client. No PowerShell process is launched for this mode.
- Checks the selected computer's C$ campaign directory and active-owner.json, validates the final postcleanup.json receipt, reads all three Task Scheduler task names via read-only COM RPC, then compares the precise 64-bit HKLM Lock Screen policy registry values to the signed-off receipt.
- Preserves the original per-host statuses **Verified**, **Pending**, **NeedsReview**, and **Unknown**; access-denied or inaccessible proof is never treated as Verified. Missing/invalid receipts or still-active owners remain visible for investigation.
- Verifies campaign identity, schema, completed file/task cleanup, receipt timestamp, exact registry value kinds, ordered multi-string values and binary data. Alternative network AD credentials continue to apply to LDAP and remote SMB/RPC.
- Adds isolated offline C# regression tests for forged/stale receipts, missing values, mismatched DWORDs, binary data and MultiString ordering.

## Previous features retained
- Full 6-step Wizard with AD connection choices, DPI-aware headers, and update notification leading directly to the Check/Install updater.
- Native directory discovery, DC Consistency, group assignment, GPO scan, managed GPO lifecycle List/Preview, SYSVOL Scan/Preview, immediate action List/Preview and read-only GPO Deployment Plan.

## Remaining legacy PowerShell actions
- Immediate Actions **Push**, **Repair**, **Report**, **Cleanup**; GPO Lifecycle **Apply**; GPO Deployment **Deploy**; SYSVOL deletion **Cleanup**; and extended GPO conflict **Effective**, **VerifyEndpoint** and **VerifyEvidence** still run on their original safeguarded PS1 engines.
- The endpoint LockScreen-Agent.ps1 remains unchanged. No actual client changes happen during VerifyCleanup.

## Testing
CI builds x86/x64 and runs offline self-tests; it does **not** provide domain-joined Windows clients, live RPC or DFSR replication. Validate VerifyCleanup first on dedicated test machines with verified postcleanup receipts. A client reported Unknown must not be counted as clean.
