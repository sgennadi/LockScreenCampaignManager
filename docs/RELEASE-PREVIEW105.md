# LockScreen Campaign Manager — 0.4.0-preview.105

## What's new

1. **AD computer activity:** OU and group counters now distinguish enabled, disabled and unknown computer account states; missing `lastLogonTimestamp` is reported separately.
2. **30 / 90 / 180 / 365 days:** Choose a read-only age window to see how many enabled accounts in the selected Computer OU subtree and directly selected security group have a replicated logon timestamp within that window.
3. **Clear uncertainty:** Unavailable DC UTC or incomplete/invalid LDAP evidence shows **Not verified**, not zero. User OU and Screen Saver targeting do not use computer logon activity.
4. **English / Русский / עברית:** Add reversible display-only labels, wrapped activity summaries and simulated 100–200% Windows UI checks; OU, group, policy and JSON identifiers remain unchanged.
5. **No deployment side effects:** New offline integrity tests verify capped read-only LDAP, monotone activity buckets, inactive/unknown counts and no change to OU/group/GPO selection or explicit deployment approval.

**Important:** AD `lastLogonTimestamp` is replicated **infrequently** (often lagging by days) and is not an accurate last sign-in, online-state check or proof of client response. The new window only changes **displayed estimates**. It does **not** exclude stale/disabled computers from AD group assignment, GPO applicability or Client Health report denominators. Direct group counts still exclude nested and primary-group memberships. Reconfirm actual GPO application and client health on approved domain-connected Windows 10/11 lab PCs. Physical 200% DPI/RTL (#36), SMB LocalSystem (#30), and JPG User GPO pilot (#25/#26) remain unverified.

[OU and group targeting guide](OU-AND-SECURITY-GROUP-TARGETING.md) · [Changelog](../CHANGELOG.md)
