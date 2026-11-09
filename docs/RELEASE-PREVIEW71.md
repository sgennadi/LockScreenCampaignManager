# LockScreen Campaign Manager 0.4.0-preview.71

## GUI: clipped fonts, panels and buttons

- Increased inner spacing and scrolling in Campaign Wizard, particularly Schedule and the Client Health / Feedback block. Bottom status lines have additional baseline room and stay above wizard navigation.
- Fixed persistent fixed-height field clipping for Client Health, Device & Repair Dashboard and GPO Operations. Shared glyph-based WinForms row calculations prevent cropped descenders in one-line labels and controls across dialogs.
- Long OU DN and UNC path values appear in textboxes with horizontal scrolling rather than truncated labels. GPO Operations keeps the full selected OU scope on its own line.
- Browse SMB and Copy path controls have been added to Feedback Settings and Client Health. The selector validates a dedicated UNC root; it never changes SMB ACLs.
- Main UI Settings now exposes 175% and 200% additional scaling. Automated WinForms smoke verifies existing registered dialogs at 100, 125, 150, 175, 187.5 and 200% simulated fonts; real monitor DPI still requires an interactive acceptance pass.

## Time zone comparison

Schedule Wizard and the Schedule editor display the **current** offset of the selected campaign time zone versus the PC's local time zone, difference in hours and minutes, and whether the selected time zone is currently in DST. Example: `Campaign: UTC+02:00 | This PC: UTC+03:00 | Difference: -1 h | DST now: No`. Calculated using `TimeZoneInfo` at `DateTimeOffset.UtcNow`, not a fixed offset. The project still stores Windows time-zone IDs; client schedules continue to handle DST rules.

## Bug Reports

Reviewed the public <https://github.com/sgennadi/LockScreenCampaignManager/issues/11> (preview.69). **Preview GPO lifecycle** without a selected managed GPO should display a guidance/no-op state rather than an `InvalidOperationException` and error-report prompt. The button is disabled until a policy is selected; a method-level guard remains as defense in depth. No domain changes occur.

The repository's daily/issue-triggered `Review published diagnostic Issues` workflow remains read-only. It groups only manually published, validated diagnostic metadata; it does not collect client logs, authentication secrets or screenshots.

## Safety, validation and limitations

- Windows x86/x64 builds and self-tests, offline AD lab contracts, PS1/native EXE version invariants, WinForms layout smoke and release SHA-256 checks are CI gates.
- Native EXE client mass deployment remains **blocked** until isolated AD/Windows tests pass; PS1 is the production default.
- GitHub-hosted runners cannot inspect live domain GPOs or confirm the visual layout on the user's monitor. Verify on Windows 11 at 100-200%, including SMB browsing and time-zone comparisons, before production use.
