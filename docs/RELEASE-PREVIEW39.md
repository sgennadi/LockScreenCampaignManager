# LockScreen Campaign Manager 0.4.0-preview.39

## UniversalUpdater interface redesign
- Replace low-contrast grey blocks with a dark navy header, crisp white release details and a clearly separated progress/status strip.
- Put release channel, installed version, architecture, optional GitHub token and repository information into a responsive two-column settings grid. Credentials remain session-only.
- Divide actions into two rows: **1 Check for updates**, **2 Download & verify**, **3 Install & restart**, then secondary Open releases, Backup status and Close. Primary actions are visually distinguished and cannot be used before prerequisites are satisfied.
- Increase the default Updater window size and apply DPI-aware layout sizing. Update WinForms smoke-test expectations for the new labels.
- Retain manual confirmation, verified staging, backup and rollback for portable user-profile EXEs; no changes to GPO or gMSA installations.
- Preserve support for Windows 10 and Windows 11 and saved per-user window placement.

## Validation
GitHub Actions builds Windows x64 and x86 portable EXEs and runs offline simulated WinForms layout tests. Real visual DPI checks and a complete physical EXE replacement/rollback still require a target Windows 10/11 machine.
