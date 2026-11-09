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

## Critical rollout limitation

**Automatic JPG User GPO deployment is not enabled** in this preview. The Group Assignments preflight explicitly rejects it. User GPO deployment must first have an approved, integrity-checked installer or file delivery strategy that doesn't run unsigned/large executables directly from SYSVOL or silently alter a user's photo settings. Testing on GitHub-hosted Windows does not establish that this SCR runs under the real client session, honors /p on the chosen Windows build, or is allowed by AppLocker/WDAC and local security policies.

Existing built-in Windows \`.scr\` settings and group targeting remain unchanged, and existing Lock Screen PowerShell/EXE agents are not modified. PhotoScreenSaver.scr is a separate local pilot and cannot be considered a production GPO deployment yet.
