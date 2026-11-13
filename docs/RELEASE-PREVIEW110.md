# LockScreen Campaign Manager — 0.4.0-preview.110

## What's new

1. **Compare with gpresult:** New **Compare gpresult XML...** action in the complete read-only **Scan all DC GPOs** report.
2. **Separate Computer and User evidence:** Independently import `gpresult /scope computer /x` for Lock Screen/power policies and `gpresult /scope user /x` for Screen Saver policies, matching each GPO by GUID and scope.
3. **Strict privacy and identity checks:** Explicit operator-entered Computer FQDN/User identity, expected domain, bounded local XML, DTD/XXE rejection, timestamp and duplicate GUID checks; SHA-256 and historical warnings. Failed imports do not replace valid prior evidence.
4. **Understand the status:** Each scanned policy displays its DC source risk separately from XML **Reported applied**, **Reported filtered**, **Insufficient flags**, **Not listed (unknown)** or **Not imported**, with independent per-scope Clear buttons and RU/HE labels.
5. **Windows release gates:** x64/x86 single-file EXE builds, offline Windows and simulated 100–200% UI regressions, plus the existing Offline AD Lab test suite.

**Limitations:** The XMLs are unsigned manual point-in-time snapshots, not remotely attested. Typed identities are not AD verified by this import; User and Computer XML may describe different hosts/sessions. Applied GPO flags do not prove a particular value is winning or the lock screen/saver actually appears. Live Windows 10/11 User+Computer gpresult, powercfg and desktop behavior remain acceptance requirements. No AD/GPO/SYSVOL/endpoint writes or automatic fixes.

[How to import gpresult](RSOP-EVIDENCE-IMPORT.md) · [Changelog](../CHANGELOG.md)
