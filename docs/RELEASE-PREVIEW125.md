# LockScreen Campaign Manager — 0.4.0-preview.125

## What's new

1. **Direct target selection after AD Connect.** A successful writable-DC connection opens the OU and security-group selection step without another Next click.
2. **OU / Users / Groups on one page.** Screen Saver and combined campaigns show their computer/user assignment editor inside the deployment Wizard; the redundant automatic modal window is removed.
3. **Documents project folder.** New local JSON projects default to the Windows Documents/LockScreen Campaign Manager folder, including redirected Documents.
4. **Save without repeated Save As.** Saving an opened or already saved project updates its existing file; Save As remains available. The last selected folder is remembered per Windows user.
5. **Safer navigation.** Changing the AD connection clears the embedded target editor; Advanced mode remains available. Preview and explicit confirmation remain mandatory before any GPO deployment.

## Update

From Preview.124, open **Check updates** and select the **Preview** channel. The updater checks available GitHub Releases on opening. Install only after reviewing What's New and approving the verified package.

## Validation and safety

The Windows x64/x86 build, offline regression suite and GUI smoke tests are required before the GitHub workflow can publish the Preview release. This release does not automatically modify AD, GPO or SYSVOL. Real-environment acceptance (including Hebrew and 200% DPI) remains separate from automated tests.
