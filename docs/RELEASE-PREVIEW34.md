# LockScreen Campaign Manager 0.4.0-preview.34

This release supersedes preview.33 and includes the final navigation and responsive-window refinements.

## Main window
- Campaign Wizard is **image first**: Add files / folder → Schedule → Review and save locally → optional Active Directory deployment.
- A non-administrator can prepare and save a local campaign without signing into AD. AD inventory is read-only; GPO changes are a separate, explicit deployment workflow.
- Contextual Advanced menus group Project, Images / schedule, Review / export, AD / GPO and Tools. Commands that cannot run yet are disabled.
- The image editor has primary Add files, Add folder and Schedule buttons plus an Arrange / edit menu for reorder, replace, sorting and filename inference.
- Lower action rows can wrap/scroll instead of clipping. Back/Next use fixed-column, text-only layout.
- Connection status and Wizard/Advanced selection use aligned columns, preventing unwanted wrapping.
- Window geometry is restored on a visible monitor; size, position and the adjustable image/preview splitter ratio are saved.
- Status text remains visible in both modes, and optional AD connection is no longer styled as a mandatory first step.

## Directory connection
- The read-only review omits unused company/OU/security-group defaults. More readable authentication error and labels.
- The encrypted Windows Credential Manager login opt-in from preview.32 remains supported.

## CI
GitHub Actions builds portable self-contained Windows x64 and x86 EXEs and runs offline synthetic AD / Windows 11 tests plus multi-scaling WinForms layout smoke. A domain-connected pilot is still required for actual GPO, SYSVOL and client behavior.

Portable EXEs are unsigned; check SHA256SUMS.txt and follow your organization's software approval policy.
