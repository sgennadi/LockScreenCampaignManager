# LockScreen Campaign Manager 0.4.0-preview.83 — optional SMB campaign payload

Preview.83 adds a choice in **AD / GPO Deployment → Campaign files**:

- **SYSVOL (default)** for the original independently copied agent, manifest and image files.
- **SMB network folder (UNC)** to publish a separate immutable campaign folder under an **existing, protected** network share instead of SYSVOL.

GPOs themselves (AD object, security group filtering, OU link, policy files and Startup metadata) **still reside in AD/SYSVOL**; only the Lock Screen campaign package moves. This works with the default PowerShell startup agent and the previously lab-restricted EXE client option. A per-group Lock Screen campaign gets its own unique package. Existing project files default to SYSVOL.

## Safety

- Read-only Preview checks the selected source's UNC syntax, root accessibility, ACL restrictions, package/agent/photo SHA-256, target collision and independent GPO conflicts.
- Apply independently repeats those checks, refuses unsafe/broad-writable network directories and existing target folders, copies/hashes the package, then registers the GPO Startup script using the verified UNC path.
- An SMB folder must be provisioned by an administrator; the Manager never creates a share or overwrites an existing campaign package. The **client COMPUTER account must have read/execute permissions** in both NTFS and share ACLs. Read-only Preview cannot prove the latter.
- Existing SYSVOL lifecycle cleanup does not automatically remove external SMB packages. After unlinking the GPO and completing verified endpoint cleanup, remove the SMB source manually.
- **No automatic JPG Photo Screen Saver User GPO deployment.** The current-user JPG and Preview.82 Computer GPO lab kit workflows remain isolated/unchanged.

Windows x86/x64 CI covers native compilation, WinForms simulated DPI checks, existing production agent regressions and new source validation tests. **Real domain/SMB client startup, sign-in/lock-screen display and RSoP have not been independently verified.** Use one isolated computer group first.

[SMB configuration and acceptance guide](SMB-CAMPAIGN-SOURCE.md)
