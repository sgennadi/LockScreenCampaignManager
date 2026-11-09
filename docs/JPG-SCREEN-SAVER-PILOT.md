# JPG Screen Saver — Windows 10/11 local pilot

## Choose JPG mode before files

In the Campaign Type Wizard select **Screen Saver** or **Both**, then enable **JPG photo slideshow (use selected campaign images)**. The Wizard keeps the Images / Arrange / Schedule pages available instead of showing "(skip)". You can select multiple JPG/PNG files or import a whole image directory, arrange them, and set **Change JPG every (s)** (5–3600 seconds).

This is separate from **Screen Saver idle timeout** (seconds until Windows invokes the screensaver). Enabling JPG does not modify existing Lock Screen campaign images or saved built-in .scr settings.

## Export and test

On the Save Wizard page choose **Export JPG .scr pilot...** and select an existing local folder. The manager creates a NEW directory containing:

- \`PhotoScreenSaver.scr\` — the current self-contained Manager application packaged as a restricted Windows screensaver executable.
- \`slideshow.json\` — bounded image order, interval, and SHA-256 for the SCR and each JPG.
- \`images/01.jpg\`, \`images/02.jpg\`, ... — normalized images with EXIF orientation handled and checksums.
- \`INSTALL-LOCAL-PILOT.txt\` — local test instructions.

On an **authorized test** Windows 10/11 computer, copy the entire directory into a protected location (including the JSON and JPGs). Verify file permissions and manually test:

\`\`\`cmd
"PhotoScreenSaver.scr" --photo-saver-self-test
"PhotoScreenSaver.scr" /s
\`\`\`

During \`/s\`, the first JPG fills the display with aspect ratio preserved (black letterboxing) and transitions at the chosen interval. Keyboard/mouse interaction exits; Windows manages the actual unlock/password behavior. Windows invokes a screensaver using the \`/s\` convention. \`/p <window-handle>\` is available for the Windows screen saver settings preview; \`/c\` shows managed-settings information.

The package is **local and read-only**: no registry writes, no task creation, no service installation, no auto deployment, no domain settings changes. The SCR process cannot enter manager/admin/client/updater command paths, even if called with other switches. The runtime checks exact JPG names, bounded sizes, SHA-256, manifest schema, file safety and executable integrity before showing anything.


## Opt-in current-user idle activation (preview.81)

After exporting the JPG .scr package in the Campaign Wizard, choose **Install local JPG pilot...** on its Save page, select the *exported package directory*, and confirm the current-user-only installation. The manager validates the image and executable SHA-256 checksums again, copies the package to the current user's LocalAppData pilot folder, saves the existing four non-policy HKCU Desktop screen-saver values, and configures Windows to run the local photo .scr after the chosen idle timeout. You do not need to copy or install in System32.

This is only for an authorized test profile with no managed Screen Saver GPO. The installer aborts if HKCU/HKLM policies already set Screen Saver values; it does not request admin rights, change GPOs, start scheduled tasks or write HKLM. Test idle activation and keyboard/mouse exit on the physical Windows desktop, then test password-on-resume and multi-monitor behavior.

Select **Remove local JPG pilot...** on the Save Wizard page to restore the saved HKCU values. The removal checks that all four settings still match what the pilot installed. If the operator or Group Policy has modified a setting since installation, it refuses an automatic overwrite; inspect settings and make changes manually rather than forcing a stale backup. Removal of the local files is also stopped if rollback cannot be verified. The local test install is **not** a supported production installation, and there is still **no automatic domain GPO delivery** for JPG .scr files.

## Manual computer GPO Startup lab kit (preview.82)

After exporting the JPG .scr package, choose **Export GPO lab kit...** from the Campaign Wizard Save page, select the exported `JPG-ScreenSaver-<campaign-id>` folder and approve the **default-No** confirmation. The manager creates a separate sibling `JPG-GPO-Lab-Kit-<campaign-id>` containing the same checked photo package, `PhotoSaver-StartupInstall.ps1` and `LAB-GPO-INSTRUCTIONS.txt`. The startup script is pinned to SHA-256 of the SCR and slideshow manifest and verifies individual JPG hashes. Existing destination folders are not overwritten.

The kit does **not** automatically create or link a GPO. Use GPMC to scope a new **TEST Computer Startup GPO** to a handful of pilot computers in the chosen computer OU/group, and stage the script together with its photo package in the GPO's startup script folder. At computer startup, LocalSystem can copy the verified content into a restricted local `C:\ProgramData\LSCM-PhotoSaver\<campaign-id>` folder. Run the script in **Verify** mode to inspect the existing installation without modifying computer/user registry settings.

A computer Startup GPO installs files, *not* a User Configuration screen saver policy. Do not enable `SCRNSAVE.EXE` user policy until pilot desktops have the appropriate local files and all target OU/security/WMI/site conditions are verified. Windows PowerShell 5.1 is used in this **lab-only installer**; it is not part of the Manager's native C# GPO operations or a mandatory client-agent mode. Automatic JPG User GPO delivery remains blocked.

## Critical rollout limitation

**Automatic JPG User GPO deployment is not enabled** in this preview. The Group Assignments preflight explicitly rejects it. User GPO deployment must first have an approved, integrity-checked installer or file delivery strategy that doesn't run unsigned/large executables directly from SYSVOL or silently alter a user's photo settings. Testing on GitHub-hosted Windows does not establish that this SCR runs under the real client session, honors /p on the chosen Windows build, or is allowed by AppLocker/WDAC and local security policies.

Existing built-in Windows \`.scr\` settings and group targeting remain unchanged, and existing Lock Screen PowerShell/EXE agents are not modified. PhotoScreenSaver.scr is a separate local pilot and cannot be considered a production GPO deployment yet.
