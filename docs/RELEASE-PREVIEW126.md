# LockScreen Campaign Manager — 0.4.0-preview.126

## What's new

1. **Safer portable updates.** Bind the extracted EXE to the verified release ZIP, verify installed bytes before the self-test, and retain the previous rollback checkpoint until the new process starts.
2. **More accurate GPO security filtering.** Handle conditional and property-specific ACL entries conservatively; preserve the selected computer's Read check for User GPOs.
3. **Loopback and policy precedence.** Rank Merge/Replace candidates using OU/domain depth, Enforced and link order; reject ambiguous or malformed Registry.pol mode settings.
4. **Screen Saver and power conflicts.** Rank supported AD/SYSVOL timer sources for the selected computer/user, compare Screen Saver with display-off, sleep and inactivity lock, and recalculate power/RDS blocker risk for the exact scope.
5. **Regression tests and UI.** Add native offline tests for update integrity, ACLs, loopback and timer precedence, plus Russian and Hebrew timer-review labels.

## Update

Open **Check updates**, select the **Preview** channel, review What's New and explicitly approve Install. The update does not automatically change AD, GPO, SYSVOL or endpoint settings. SHA-256 integrity is not publisher authentication.

## Validation and limitations

The AD-only analyzer estimates applicability and source priority; it does not establish actual endpoint RSoP, WMI/GPP targeting, active power plan or visible Screen Saver behavior. Optional endpoint evidence remains available in the same analyzer. Custom/JPG automatic User GPO deployment restrictions remain unchanged. Real Windows 10/11 domain, rollback, and 200% DPI/RTL acceptance remain open. The release workflow must pass its required Windows build and package verification before publishing.
