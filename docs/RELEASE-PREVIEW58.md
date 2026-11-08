# LockScreen Campaign Manager 0.4.0-preview.58

## Fixed lower navigation and Windows time zones
- **Main editor:** Back/Next and Project/Tools actions live in a pinned bottom footer outside the scrollable content. Enlarged header, campaign name, schedule fields or a smaller RDP/laptop screen no longer push the action buttons below the visible area.
- **Campaign Wizard:** the bottom Back/Next/Close footer is separately bottom-docked and sized against the usable monitor working area at high DPI. Wizard pages scroll independently under the fixed navigation; saving the project can be skipped with explicit choice.
- **Windows time zone in the main editor:** expands the field to about 350px and the drop-down to at least 530px for longer IDs, with autocomplete. The settings row grows to fit instead of covering Campaign name.
- **Campaign Wizard / Schedule:** adds a visible, full-width, selectable Windows time zone on the Schedule page. Choosing a zone immediately updates both the unsaved campaign and main editor; local scheduled wall-clock times are not silently shifted. The user can review and change the schedule before saving.
- **Edit schedule dialog:** offers the same Windows time-zone choices and persists selected zone to the draft and main editor upon Apply.
- Uses one shared ordered Windows zone ID list. Keeps any imported IANA ID for verification rather than silently changing it.
- **DPI tests:** check pinned Wizard footer actions, complete zone pickers and a simulated direct zone switch that must appear in the in-memory project and editor at 100%, 125%, 150% and 187.5% font-scale profiles.

## Other functionality retained
- Compact new-release indicator opens Updater with fresh Check then user-authorized Install.
- Native C# read-only AD inventory, GPO and SYSVOL scans, lifecycle preview, immediate actions List/Preview and post-cleanup VerifyCleanup remain available.
- Protected GPO/SYSVOL writes and remote actions still use embedded PowerShell engines; the Windows LockScreen-Agent.ps1 is unchanged.

## Validation limits
Hosted GitHub Actions builds and offline tests cannot reproduce an interactive user's exact display/DPI or domain controller. Test the Wizard and editor on Windows at 125%, 150% and 200% DPI, and verify an Israel Standard Time schedule is exported with the selected ID before deployment to a test OU. Keep production GPO changes behind Preview/Apply confirmation.
