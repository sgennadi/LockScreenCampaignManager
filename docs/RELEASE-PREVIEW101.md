# LockScreen Campaign Manager — 0.4.0-preview.101

## What's new

1. **Keep selected groups:** Choosing or changing an OU no longer silently clears an already-selected Computer or User security group.
2. **Clear All-groups fallback:** If the group is outside the OU's direct membership list, the Manager keeps it and switches visibly to **All groups**, with an applicability warning.
3. **Saved targets survive:** Switching between Lock Screen and Screen Saver assignment rows restores the exact saved Computer/User group DNs; company-default groups also remain selected.
4. **Regression checks:** Add offline membership-selection tests and WinForms checks for independent Computer/User groups, including mixed-case DNs.
5. **GPO safety unchanged:** Direct membership filtering remains read-only; the target OU, rights, conflicts and exact group security filtering still require Preview and confirmation before Deploy.

**Limitations:** This is a targeting UI fix, not evidence of real effective GPO application. Nested groups, primary groups, deny ACLs, WMI filters and user loopback must be verified independently using approved lab AD/RSoP. Real physical 200% DPI/RTL testing (#36), SMB LocalSystem endpoint (#30) and JPG User Screen Saver pilot (#25/#26) remain open.

[OU and group targeting guide](OU-AND-SECURITY-GROUP-TARGETING.md) · [Changelog](../CHANGELOG.md)
