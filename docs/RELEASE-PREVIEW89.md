# LockScreen Campaign Manager 0.4.0-preview.89 — truthful SMB preflight evidence

Preview.89 corrects a misleading deployment-preview behavior: when campaign content was hosted in **SMB instead of SYSVOL**, the GPO Deployment Wizard still scanned **SYSVOL duplicates** and displayed those figures beside the new SMB campaign. That comparison is irrelevant to the chosen source and did not prove computer-account network access.

## Changes

- For **SYSVOL mode**, preserve the existing optional read-only Smart SYSVOL duplicate-image scan.
- For **SMB mode**, **skip** that duplicate-image scan entirely, and report the actual **validated UNC source root, unique immutable campaign package path and client Startup script path**.
- Distinguish four independent outcomes in the native read-only GPO Plan:
  - **NTFS ancestor broad-write Allow-ACE scan: PASS (ADVISORY)**. This is only a limited DACL inspection; it is not an effective permission or secure-path guarantee.
  - **SMB share-level permissions: NOT TESTED**.
  - **Effective computer-account READ: NOT TESTED**.
  - **Network availability during Windows startup: NOT TESTED**.
- Fail closed if a Plan response omits or misstates these fields or reports a mismatched UNC package path. The same evidence wording applies to **single-target and multi-group Lock Screen previews**.
- Show the exact UNC root, limitations and endpoint canary requirement again in the **final GPO creation confirmation**, for both single-group and multi-group deployments.
- Preserve the validated SHA-256 package, existing project source choice, PS1/EXE client selection, GPO scope/security controls, and SYSVOL default behavior.
- Add pure offline C# self-tests and GitHub Actions source contracts for the advisory-vs-verified distinction, stale/inconsistent evidence rejection, and no SYSVOL duplicate scan in SMB mode.

## Required real-world acceptance

An administrator reading the share does **not** establish the SMB rights of `DOMAIN\COMPUTER$`. Test only on an isolated, explicitly approved Windows 10/11 domain client: confirm DNS, SMB share and NTFS Read & Execute, LocalSystem ability to read the **pinned SHA-256 package**, availability at actual computer startup, computer GPO RSoP, visual Lock Screen operation and independent final cleanup. Use [SMB SYSTEM canary](SMB-SYSTEM-CANARY.md) and [endpoint closure audit](ENDPOINT-CLOSURE-AUDIT.md). Neither GitHub-hosted CI nor the manager's read-only preflight is a substitute.

The separate JPG Photo Screen Saver **automatic User GPO** remains blocked until its real Windows 10/11 acceptance issues #25 and #26 are completed. No automatic GPO or SMB writes are performed when reviewing the Preview.
