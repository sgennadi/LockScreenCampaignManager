# LockScreen Campaign Manager — 0.4.0-preview.124

## What's new

1. **One navigation action on the Review & save step.** Remove the three upper buttons *Validate campaign*, *Save project...*, and *Skip saving & continue*. The bottom **Next** always validates the campaign before advancing to Deployment.
2. **Optional local project via a checkbox.** Add **Skip saving a local project (.json)** beside the campaign name. Unchecked (default): **Next** validates and opens the normal project Save dialog if the current draft is not already saved. Checked: **Next** validates and proceeds without opening a Save dialog. If saving is cancelled, the wizard stays on this step.
3. **Storage is not project saving.** Keep the separate SYSVOL/SMB selector for where the agent and campaign images will be deployed. Choosing SYSVOL/SMB does **not** save a JSON project or write to AD. Name/checkbox appear before storage selection; remove the otherwise empty upper action strip for ordinary campaigns.
4. **Consistent layout and translations.** Rename step six **Review & save**, use the same bottom Back/Next/Close navigation, and translate new captions into Russian and Hebrew. Retain specialist JPG pilot tools without removing their explicit safety confirmations.
5. **Regression gates.** Test Screen Saver-only mode with no images, normal campaign and JPG pilot flows; verify checkbox state/Next semantics and layout at simulated high DPI. Add fail-closed offline source checks and Windows x64/x86 build gates.

6. **One-click update check.** Every explicit **Check updates** entry (main menu, visible status button, or Wizard notice) opens the updater and immediately performs one read-only GitHub release check. After the response, **Check again** offers a manual retry. Only the separate **Install** button can download/verify and ask for approval; nothing installs automatically.
7. **Remember the Save checkbox.** The Wizard's local .json skip choice persists per Windows user in the existing atomic `ui-settings.json`, even after closing the Wizard or restarting the app. Applying language/DPI settings preserves the choice. Existing settings default to unchecked. CI-only UI smoke tests never persist their toggle operations.

## How it works

On **Step 6 of 7 — Review & save**, set a campaign name, decide whether to skip a local **.json** file, choose the deployment content location (SYSVOL or an existing SMB UNC share), then click **Next** in the bottom navigation. No extra Save/Skip buttons are necessary. Saving a local project is separate from exporting/deploying the campaign package.

The wizard performs the existing `CampaignSchedule.Validate` check before both paths. An unchecked checkbox may open a standard Save dialog; cancelling leaves the wizard on Step 6. A checked checkbox does not skip validation and does not create a local file. After Next, Step 7 offers deployment or an offline finish. It does not silently deploy GPO.

## Safety

This is a UX/workflow change only. It does not write to AD, GPO, SYSVOL or client registries, and does not change the existing checked/blocked deployment logic. It retains the protection against stale saved-project storage choices. Windows 10/11 real-client acceptance and Hebrew RTL/200% DPI checks are still required beyond automated smoke tests.
