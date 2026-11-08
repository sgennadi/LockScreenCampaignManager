# LockScreen Campaign Manager 0.4.0-preview.57

## Fix: Campaign name label and input visibility
- Corrects clipping of **Campaign name** and the neighbouring campaign settings labels on the main editor at Windows high-DPI/font scales. A fixed 26px caption row and 98px panel previously hid the bottom of text.
- Measures caption and input control font metrics independently, resizes each field's two rows and its outer settings area, and increases the editor's scrollable content height. When fields wrap to another line, the settings section expands rather than hiding them.
- On Campaign Wizard > Save, the **Campaign name** label, textbox and summary rows now grow with the selected font/DPI; the page's body scrolls when needed.
- Adds layout regression checks for the campaign name caption and edit control at 100%, 125%, 150% and 187.5% simulated font-scale profiles. A real 200% Windows/RDP visual check is still recommended.

## Other behavior unchanged
- Full six-step Campaign Wizard with explicit Active Directory sign-in choices, images, arrange, schedule, save and deploy.
- A compact amber update indicator with release version and notes; clicking it opens Updater for fresh Check then user-authorized Install.
- Native C# directory discovery, group assignment, GPO lifecycle read-only List/Preview, deployment Plan, SYSVOL Scan/Preview, Immediate List/Preview and VerifyCleanup continue from preview.56.
- The write operations for GPO/SYSVOL and remote machines remain on their existing guarded PowerShell engines; LockScreen-Agent.ps1 on managed clients is unchanged.

## Validation
CI builds portable Windows x64/x86 executables, validates package and hash contracts and runs WinForms layout smoke tests. Tests cannot recreate every local Windows DPI/font rendering combination or production AD/DC environment.
