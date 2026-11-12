# Native Windows DPI 200% and language visual acceptance — Preview.102

This is an **operator-led UI acceptance checklist**, integrated in the portable Manager EXE. It is **not** a replacement for physical Windows acceptance, a GPO RSoP result or a production deployment.

## How to use

1. On an approved Windows 10/11 desktop or test VM, launch the published Manager. Leave any unsaved campaigns unchanged; no AD connection is needed.
2. Select **Tools → Visual DPI / language check...** (also under **Help / tools**). Move the check window to a physical monitor that Windows has configured to **200%** display scaling (192 effective DPI). A local console session is required to save a passing observation. RDP/terminal sessions are never silently accepted.
3. Click **Refresh display**. The value is taken from the actual visible check window via the native Windows `GetDpiForWindow` call. An unrelated 200% monitor is not sufficient.
4. Optionally click **Run offline UI smoke**. This executes the current portable EXE with `--ui-smoke-test`; it runs simulated WinForms layout tests with no AD/GPO/SMB modifications. Allow the test to finish. A successful simulated test does **not** prove actual screen fit.
5. With the real Manager windows open on the same 200% monitor, switch **English → Русский → עברית → English** using Settings. Examine all seven Wizard pages, AD Deployment Browse, User/Computer group assignment, Client Health, Updater, Hebrew RTL and mixed technical LTR fields. Select each of the **six checkboxes only after personally observing it**.
6. The **Save local report** button becomes available only after the offline smoke passed, the check window reports 192 DPI from a local console and all six items have been manually confirmed. No report is saved merely by opening the dialog or running CI.

### Local evidence and privacy

Optional result path:

`%LOCALAPPDATA%\LockScreenCampaignManager\VisualAcceptance\visual-<random>.json`

It records the installed version, UTC observation time, OS build, actual check-window DPI, approximate scale, local-console flag, offline smoke result and six checkbox IDs/results. It does **not** store computer/user names, DC details, OU/group DNs, project data, screenshots, credentials, paths to files or GPO identifiers. **Nothing is transmitted.** A reparse-linked report directory is refused.

The result marker `LOCAL_OPERATOR_OBSERVATIONS_RECORDED_NOT_ATTESTED` means only that a local operator checked all six items. It is **unsigned, unauthenticated local evidence**, not proof that the full UI was physically correct or that a GPO actually applied. A clean Windows hosted runner or simulated 200% font test must never close the real DPI/RTL acceptance issue automatically.

Do not share a report unless you first review it. The operator can delete the report locally at any time.

### Separate domain tests

For OU/group assignment applicability, use read-only **Preview** and `gpresult` on a dedicated test user/computer. The Screen Saver USER policy and Lock Screen COMPUTER policy remain independent. The JPG User GPO pilot (#25/#26) and LocalSystem SMB client pilot (#30) are still separate, blocked until actual endpoint evidence exists.

This tool does not change DPI settings, company profiles, AD, GPO, registry, local scheduled tasks or client agent state. It does not require PowerShell. Physical DPI/RTL acceptance (#36) remains open until manual test observations are reviewed.
