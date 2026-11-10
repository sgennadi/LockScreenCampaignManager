# LockScreen Campaign Manager 0.4.0-preview.87 — Wizard storage and Update channel HiDPI fix

Two operator-visible fixes based on actual Windows screenshot feedback:

## 1. Campaign Wizard → 7 Deploy → Campaign files

Choose **SYSVOL (default)** or **SMB network folder (UNC)** directly in the main seven-step Wizard. For SMB, type `\\fileserver\Campaigns` or use Browse to choose an existing dedicated UNC share folder. The network editor is visible in SMB mode, and the selection remains in the in-memory campaign project. When you open **GPO Deployment Wizard**, the same selection is prefilled, and subsequent storage changes there are returned to the main campaign editor. Existing saved projects without a source field keep SYSVOL.

The SMB source is the agent, manifest and images only: **GPO metadata remains in AD/SYSVOL**. SMB and NTFS ACLs require computer-account READ and administrator-only WRITE; existing preflight and GPO security checks remain in force. An invalid UNC source prevents opening deployment. No automatic share creation, GPO modifications, or network writes occur when choosing the storage option.

## 2. Software updates → Release channel

Fix the clipped `Preview + stable` dropdown shown at high Windows DPI. Label and input rows are now measured after applying the chosen fonts, including native ComboBox `PreferredHeight`, with an automatic fit on font and DPI changes. Preserve the two visible operations **1 Check** and **2 Install**, verification results, backup restoration and unsigned-release warnings.

Windows x64 and x86 CI verifies both updates with six simulated UI scale/font profiles, native self-tests and release ZIP SHA-256 checks.

**Still unverified in hosted CI:** real domain SMB machine-account reachability at startup, GPO effectiveness, physical Lock Screen display and client cleanup. Automatic JPG Photo Screen Saver User GPO delivery remains blocked.
