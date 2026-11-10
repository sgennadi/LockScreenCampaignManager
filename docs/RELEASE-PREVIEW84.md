# LockScreen Campaign Manager 0.4.0-preview.84 — isolated SMB SYSTEM verification

Preview.84 extends the **SYSVOL / dedicated SMB content source** choice from Preview.83 with a read-only endpoint acceptance kit.

- Native C# GPO Plan now exposes the exact SHA-256 of the exported **campaign.json** and client **LockScreen-Agent.ps1/.exe**. SMB plans reject a manifest without a pinned agent digest.
- During an explicit SMB Deploy, the copied immutable folder is verified again, including **all JPEGs**, before a new GPO is registered and once more before the Startup metadata is written. The deployment receipt contains source mode, UNC path and both source SHA-256 digests.
- The offline lab ZIP includes **run_smb_canary_local.ps1** and **smb_system_canary_probe.ps1**, plus [SMB SYSTEM canary instructions](SMB-SYSTEM-CANARY.md). On a disposable domain-joined Windows 10/11 machine an administrator approves a temporary **local** SYSTEM Scheduled Task. The probe reads the exact SMB manifest, agent and every image, and compares an optional installed local copy, without executing source code or changing policies.
- The temporary task and private config are removed after the probe. A compact report remains locally under a protected ProgramData directory with no domain/computer names, UNC paths or raw GPO output. **Nothing is sent automatically**.
- Tests cover the native digest contract, PowerShell 5.1 parsing, no remote execution, offline ZIP integrity, x64/x86 EXE build, and WinForms DPI smoke.

**Not a production acceptance.** A successful source read from LocalSystem does not prove the SMB folder was available at *boot*, that a Startup GPO applied, that the Windows Lock Screen visibly changed, or that cleanup/rollback restored policy. Those checks still require a local real-domain Windows 10/11 test and manual GPO RSoP evidence.

JPG Photo Screen Saver remains a **separate** local/Computer Startup lab pilot; automatic User JPG Screen Saver GPO authoring stays blocked. Native EXE client domain rollouts remain laboratory gated. Existing SYSVOL deployments and their behavior are unchanged.
