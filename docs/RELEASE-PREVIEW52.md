# LockScreen Campaign Manager 0.4.0-preview.52

## Native C# GPO Lifecycle Preview with legacy Apply compatibility
- GPO Operations **Preview** now reads selected DC RootDSE, AD GPO state, selected OU and all domain/site links, managed startup script, GPT version and SYSVOL SHA-256 using signed/sealed native LDAP and SMB.
- Supports Disable, Enable, Unlink, Relink, Delete and Reuse read-only previews. Validates selected writable DC, exact managed GPO GUID, duplicate links, other OU/site links, and AD/SYSVOL version consistency.
- Reuse validates the replacement campaign, JPEG SHA-256, new campaign schedule and timezone, expired prior campaign, unused target SYSVOL campaign ID and DC clock skew.
- **Apply remains unchanged:** the embedded PowerShell engine independently recomputes the plan hash and all current guards before writing. Native Preview produces the exact original UTF-8 SHA-256 preimage, preserving required manual GUID confirmation.
- Includes native .NET self-tests and a Windows PowerShell 5.1 test that extracts the REAL legacy `Plan` and `Hash` functions, mocks only files, and cross-checks fixed golden values for Disable and Reuse.

## Retained protections
- No writes occur during Preview. Delete still requires all OU, domain and site links to be removed. Reuse preserves the existing GPO GUID and security filtering.
- Native GPO Lifecycle List, SYSVOL Scan/Preview, directory discovery, DC Consistency, computer/group membership, and basic GPO conflict Scan remain available.
- GPO Apply and deployment, SYSVOL actual Cleanup, Immediate Actions and extended effective-policy verification remain on legacy PowerShell pending separate native write-path implementation. The client LockScreen-Agent.ps1 is unchanged.

## Domain validation
Hosted Windows x86/x64 tests cannot reach a real AD or verify DFSR replication. First compare the native preview hash and proposed changes to the previous PowerShell preview in a **test OU and a test GPO**; only then try confirmed Apply. A mismatch must cancel the operation rather than bypassing verification.
