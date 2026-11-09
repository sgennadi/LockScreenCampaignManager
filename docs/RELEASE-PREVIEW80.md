# LockScreen Campaign Manager 0.4.0-preview.80 — JPG Screen Saver local pilot

This preview introduces an actual \`.scr\` JPG slideshow package, while leaving existing built-in Windows screen savers and Lock Screen deployments compatible.

- **Wizard**: Screen Saver → **JPG photo slideshow** enables the image-selection, arrangement and schedule pages (built-in .scr still skips them). Choose JPG/PNG files or a directory, interval 5–3600 seconds, idle timeout and password-on-resume policy.
- **Export JPG .scr pilot...** creates a restricted, self-contained PhotoScreenSaver.scr with normalized photos and SHA-256 verified metadata. The SCR's \`/s\`, \`/p\` and \`/c\` entrypoints cannot invoke administrator, client or updater behavior; \`--photo-saver-self-test\` verifies its contents without GUI.
- **Windows CI**: test both x64/x86 builds, verify a copied/renamed SCR actually starts as a separate process, verify SHA-256 rejection of modified JPG and the proper Wizard steps.
- **Safety**: this package is for local Windows 10/11 pilot testing only. **Automatic JPG User GPO deployment is intentionally blocked**, until source placement, user ACL, signature, network availability and user-session behavior have been evaluated on a real endpoint. Existing built-in Screen Saver GPOs continue to deploy normally.

This release does not update the PowerShell/EXE Lock Screen client agents and does not touch AD/GPO in tests. See [JPG Screen Saver pilot](JPG-SCREEN-SAVER-PILOT.md).
