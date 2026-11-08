# Windows 10 and Windows 11 Client Agent Support

LockScreen Campaign Manager deploys a **Windows PowerShell 5.1 client agent** to domain computers using Group Policy startup and scheduled tasks. The **manager EXE** and the **client agent** have different requirements. This document covers the client only.

## Supported client editions

- Windows 10 Enterprise / Education, build 14393 (version 1607) or later, including Enterprise LTSB/LTSC variants
- Windows 10 IoT Enterprise, including applicable LTSC builds
- Windows 11 Enterprise / Education / IoT Enterprise, including applicable LTSC builds

The same GPO and agent manage daily lock-screen images, verify hashes, enforce schedule, restore prior registry settings and remove managed files after the campaign.

**Not supported by this GPO-based design:** Windows Home / Pro editions. These do not reliably honor the forced lock-screen image Group Policy setting. Personalization CSP through MDM/Intune is a separate mechanism and is not implemented here.

The minimum build is **14393**. Unsupported/unknown OS metadata fails closed before installation or test-mode registry modification. The agent runs as SYSTEM or an elevated administrator and uses the machine policy location `HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization`.

## Windows 10 lifecycle consideration

General Windows 10 support ended on **2025-10-14**. Some Windows 10 devices may be receiving Extended Security Updates (ESU) and certain LTSC/IoT Enterprise releases have distinct lifecycle dates. Operating-system lifecycle support and campaign-agent compatibility are independent; administrators remain responsible for Windows servicing.

## Verification boundary

CI runs `tools/test_windows_client_compat.ps1`, which extracts the **production** `Assert-SupportedWindows` function and uses mocked OS version/edition metadata to verify Windows 10 and Windows 11 gates under Windows PowerShell 5.1. The hosted agent lifecycle also checks image rotation, hash validation, backup/restore and cleanup in an isolated test hive.

**CI does not claim real Windows 10 GPO/SYSTEM application verification.** Before mass deployment, test a pilot Windows 10 Enterprise/LTSC machine joined to the domain: verify SYSVOL reachability, startup task, image display, daily rotation, result feedback and restoration on cleanup. Repeat on Windows 11.

For both systems, check for competing GPO, MDM and Windows Spotlight settings before rollout.
