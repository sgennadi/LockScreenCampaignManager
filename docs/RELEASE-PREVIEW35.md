# LockScreen Campaign Manager 0.4.0-preview.35

## Client agent: Windows 10 support
- Remove the incorrect Windows 11-only client gate.
- Accept Windows 10 Enterprise/Education/IoT Enterprise (including compatible LTSC/LTSB), starting with Windows 10 build 14393 (1607), alongside Windows 11 Enterprise/Education/IoT Enterprise.
- Retain safe failure for Windows Pro/Home and unsupported or unreadable OS build metadata; GPO forced lock-screen imagery is edition-restricted.
- Preserve existing scheduled image rotation, verified local file restore, active-owner conflict protection, cleanup and telemetry mechanisms.
- Add Windows PowerShell 5.1 tests exercising the **production** OS gate against Windows 10 and Windows 11 build/edition fixtures.

## Manager interface
Includes all image-first Wizard and contextual Advanced UI changes from preview.34. No new Windows 10 requirement is placed on the management EXE; the compatibility change concerns the **client agent installed by GPO**.

## Limitations
CI validates build/edition logic and sandboxed client lifecycle, **not** visible lock-screen rendering on physical or virtual Windows 10 clients, Group Policy processing or domain-connected SYSTEM task execution. Pilot on at least one Windows 10 Enterprise/LTSC machine and one Windows 11 Enterprise machine before broad rollout.

Windows 10 general support ended on 2025-10-14; keep devices on an appropriate security-servicing path (ESU or a supported LTSC lifecycle) where applicable.

Full support matrix and pilot checklist: [Windows 10 client compatibility](WINDOWS10-CLIENT-COMPATIBILITY.md).

Portable manager EXEs are unsigned, self-contained x86/x64 Windows executables; see SHA256SUMS.txt.
