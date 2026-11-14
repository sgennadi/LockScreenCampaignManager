# LockScreen Campaign Manager — 0.4.0-preview.116

## What's new

1. **Screen Saver Browse no longer clipped.** The button `Browse .scr / .exe...` occupies its own auto-sized second row instead of a fixed 155-pixel column. ComboBox selection, custom SCR/EXE validation and local-pilot-only security behavior remain unchanged.
2. **Assignment editor labels wrap.** User/Computer OU and security-group captions now use content-measured field heights and vertical scrolling, rather than fixed 34-pixel label rows. Selection, filtering and per-scope GPO behavior are unchanged.
3. **Modern warnings show complete captions and details.** Dialog header/title, hint and action footer grow with translated text and DPI. The message body retains visible space, starts at its first line and remains scrollable/selectable. Esc/X/No/Cancel still never approve sensitive actions.
4. **AD deployment title/version fit.** The version column is measured by the full Preview build string, and the main heading can wrap without ellipsis. Header height adapts to font changes while credentials and authorization logic remain untouched.
5. **Broader high-DPI regression matrix.** CI checks all existing offline WinForms screens, and separately exercises the screenshot-affected Wizard, assignment editor, AD/GPO deployment and warning dialogs in **English, Russian and Hebrew at 100%, 150% and 200%**. The actual screenshot-specific `Check target assignments` warning is included.

## How to verify

On Windows 11 (ideally 200% DPI) open the main **Campaign Wizard → Campaign type → Screen Saver** and inspect the **Browse .scr / .exe...** button, the screen saver note and the preview/version labels. Open **User OU / groups...**, inspect the Screen Saver User security group labels, deliberately leave a required group empty and click **Save assignments**: the **Check target assignments** dialog should display its complete message and **OK** without overlaps. Then open **Active Directory / GPO Deployment** and confirm its full title and version are readable. Repeat in Russian/Hebrew and at 100% DPI.

For offline automated checks use `LockScreenCampaignManager.exe --ui-smoke-test` on a Windows interactive test runner. The CI smoke checks controls/font-metric bounds; **real 200% hardware DPI and human visual acceptance** still require the pilot desktop.

## Safety

No changes to AD, GPO policy selection, update approval semantics, local Screen Saver execution/deployment restrictions, account credentials or deployment workflow. This release corrects WinForms layout and tests only.
