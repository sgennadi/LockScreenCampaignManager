# LockScreen Campaign Manager 0.4.0-preview.88 — SYSVOL/SMB save consistency

Preview.87 added **Campaign files: SYSVOL or SMB (UNC)** to Step 7 of the main Campaign Wizard. It exposed a workflow error: someone could save the local project on Step 6, change the source or UNC folder on Step 7, and then deploy using the new in-memory source while the saved JSON still referenced the old storage.

## Updated seven-step Campaign Wizard

- **Step 6 — Save / Skip** now contains the same Campaign files selector and UNC textbox/Browse as Step 7, so storage can be configured **before** saving the JSON project. Both widgets edit exactly the same project properties.
- **Step 7 — Deploy** keeps the selector, includes **Save project changes...**, and warns when a previously saved project's storage choice is stale. Finishing offline or opening AD deployment with stale JSON requires an explicit confirmation; saving again clears the stale warning.
- A nested AD/GPO Deployment Wizard storage change is copied back to the main editor. If it diverges from the previously saved JSON, the main Wizard marks that project as unsaved rather than claiming the existing file was updated.
- **Skip saving & continue** remains voluntary: the session can deploy using validated in-memory settings without writing any .json project. A previously saved file is never silently overwritten.
- SYSVOL remains the default for legacy projects. For SMB, the dedicated UNC path must pass local validation before GPO deployment; GPO metadata remains in AD/SYSVOL.

## Verification and safety

Windows CI checks WinForms layout at 100%, 125%, 150%, 175%, larger fonts and 200% for both storage-selection screens, UNC textbox and Browse, and retains the existing Update channel DPI tests and x64/x86 release archive checks. A real test of domain share ACLs, LocalSystem startup, visible lock screen and rollback is **still required**.

No AD/GPO/SYSVOL/SMB write is performed just by choosing the source. JPG Photo Screen Saver automatic User GPO is still blocked pending real Windows 10/11 domain tests.
