# LockScreen Campaign Manager — 0.4.0-preview.103

## What's new

1. **OU and Group targeting translated:** RU/HE/EN labels for OU-first direct-member groups and **All domain groups (group first)**. Names, DNs and SelectedIndex are never translated or modified.
2. **Screen Saver vs Lock Screen:** User OU / User Group for Screen Saver, Computer OU / Computer Group for Lock Screen. Both remains two independent GPO scopes; no automatic loopback.
3. **Readable AD Deployment:** Localized targeting warnings, connected-DC statuses, stored company defaults and group-membership totals, with the same explicit Preview/Deploy safety checks.
4. **High-DPI group editor:** Shorter visible OU/group filters, full explanatory tooltips, and automatically wrapping headings/help for English, Russian and Hebrew at simulated 100–200% DPI.
5. **Regression and safety checks:** Offline RU/HE dictionary, reverse-translation and format-placeholder validation; native Windows x86/x64 UI smoke and no-overwrite checksum-gated release publication.

**Limitations:** This is a UI and localization update only. Direct memberOf does not prove effective GPO application or expand nested/primary memberships. GPO linkage is to an OU, not a group; Security Filtering is separate. Real Windows 10/11 physical 200% DPI + RTL acceptance (#36), SMB LocalSystem pilot (#30) and JPG Screen Saver User GPO rollout (#25/#26) still require supervised domain test evidence.

[OU and group targeting](OU-AND-SECURITY-GROUP-TARGETING.md) · [Changelog](../CHANGELOG.md)
