# Real Windows 10/11 display acceptance — 200% (Preview.100)

This is a **local, manual visual test**, not a GitHub-hosted Windows test.
The purpose is to resolve the physical DPI/RTL acceptance gap tracked by issue #36.

## Preparation

1. Use a disposable Windows 10/11 desktop or VM with a **local console session** (not RDP). Start with the exact official Preview.100 portable EXE downloaded from GitHub Releases. Validate the **release archive SHA-256** against the published SHA256SUMS.txt, and extract it to a local folder. Do not download an executable from an unknown source.
2. Download the matching Preview.100 **Offline AD + Windows 11 Lab Kit** from the successful **main** branch GitHub Actions run. It includes \`tools/run_ui_visual_acceptance.ps1\` and \`tools/LscmDisplayMonitorInterop.cs\`. Those files must stay alongside one another.
3. In Windows Display Settings, set a monitor to **200%** scaling. Put the Manager window on that monitor. You can repeat at 100%, 125%, 150% and 175% separately; this local acceptance report specifically requires 200%.
4. Open a **local Windows PowerShell 5.1 console** as the interactive test user. Neither administrator rights nor domain privileges are needed. Before running the test, ensure no active campaign editing or unsaved work is open in the Manager.

## Execute locally

From the lab-kit directory, replace TESTPC01 and the EXE path with the actual local test machine and extracted, trusted program:

\`\`\`powershell
powershell.exe -NoProfile -File .\tools\run_ui_visual_acceptance.ps1 -ExpectedComputer "TESTPC01" -ManagerExePath "C:\Lab\LockScreenCampaignManager.exe"
\`\`\`

Type **I_APPROVE_UI_VISUAL_TEST** only after reviewing the local EXE. The script:
- refuses non-console/service/CI/RDP sessions, wrong computer names, Windows Server, missing or linked EXEs, and missing interop source;
- reads effective Windows monitor scale with \`GetScaleFactorForMonitor\` without changing DPI settings;
- insists at least one monitor is exactly **200%**;
- runs only \`LockScreenCampaignManager.exe --ui-smoke-test\` (offline, no AD/SMB/GPO) and checks its exit code;
- then **asks you** to open the actual Manager on the 200% monitor, switch **English → Русский → עברית → English**, and inspect the Wizard, AD connection, Updater, Client Health, mixed left-to-right fields and bottom navigation. Answer **YES** only for things you personally observed.

## Results and limits

The private JSON report is stored beneath:

\`%LOCALAPPDATA%\LockScreenCampaignManager\UiVisualLab\ui-visual-<random>.json\`

It records **monitor scale percentages, OS build, smoke-test result and six YES/NO observations**. It does **not** record computer name, username, path, domain, GPO identity, credentials, screenshots or full error messages. **Nothing is uploaded.** Delete the local report when your lab review is complete.

\`LOCAL_OPERATOR_OBSERVATIONS_RECORDED_NOT_ATTESTED\` means the local operator confirmed all six prompts. This is **unsigned evidence only**, not a cryptographic attestation, and does not prove that a remote user or device really passed. \`UI_SMOKE_FAILED\`, \`NOT_READY_200_PERCENT\` and \`OPERATOR_REVIEW_INCOMPLETE\` must not be accepted as a pass. A successful GitHub-hosted Windows check also does **not** establish physical screen acceptance.

No automatic creation/linking of JPG User GPO is enabled. The independent SMB LocalSystem/GPO and PhotoScreenSaver acceptance procedures remain in [SMB-SYSTEM-CANARY.md](SMB-SYSTEM-CANARY.md) and [JPG-SCREEN-SAVER-PILOT.md](JPG-SCREEN-SAVER-PILOT.md). Issues #30, #25 and #26 remain blocked until actual approved test endpoint evidence exists.

**Safety:** This tool never creates a GPO or modifies registry, scheduled tasks, screensaver policy, Active Directory or the SMB share. Only the local JSON report is written. Use a dedicated test system, never a production DC.
