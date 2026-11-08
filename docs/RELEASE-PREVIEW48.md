# LockScreen Campaign Manager 0.4.0-preview.48

## Native C# GPO conflict scan (basic Scan mode)
- Read-only inherited GPO scan now uses native signed/sealed LDAP, exact selected-DC SYSVOL file reads, a bounded Registry.pol binary parser, and a DTD-protected GPP Registry.xml parser.
- Evaluates disabled/enforced links, blocked inheritance, disabled computer GPO portion, WMI-filter presence and potentially overlapping Windows Lock Screen / Spotlight policy settings.
- Optional read-only Remote Registry check is limited to domain computer FQDNs, with alternate credentials applied to network reads.
- Missing or unreadable linked-GPO SYSVOL Machine data and malformed policy files fail closed, rather than reporting an unverified clean result.
- Adds in-memory self-tests for OU and GPO scoping, escaped DN commas, policy parsing, corrupted binary files and XML DTD rejection.

## What has not changed
- **Effective**, **VerifyEndpoint**, and **VerifyEvidence** modes still use the existing PowerShell-based GPO conflict engine, with their prior scoped verification safeguards. Therefore that embedded resource remains in the EXE.
- Other remaining PowerShell administrative modules: GPO deployment, lifecycle, immediate client actions and SYSVOL storage. Client-side LockScreen-Agent.ps1 is unchanged.
- Native AD discovery, DC Consistency and Computer & Group Assignment remain on C# from earlier previews.
- The updater remains two-step **Check → Install**.

## Validation
Windows hosted x86/x64 builds, offline unit tests and synthetic UI checks do not substitute for a controlled domain pilot. Compare C# and historical Scan findings against a test OU with enforced, disabled, and conflicting policies before production GPO deployment.
