# LockScreen Campaign Manager — 0.4.0-preview.100

## What's new

1. **OU-based group filtering:** Selecting an OU shows security groups with direct COMPUTER members in that OU and its child OUs. Groups may be stored elsewhere in Active Directory.
2. **Group-first option:** Switch to **All domain security groups** to choose a group first or work with nested memberships; explicitly select an OU before linking a GPO.
3. **Screen Saver users:** Group assignments offers an independent USER OU/USER security-group filter. Lock Screen continues to use COMPUTER OU/group. Both remains two independent GPOs.
4. **Cleaner AD Deployment:** Show each OU's parent path to distinguish identical names, remove exact DN duplicates and keep SMB **Browse...** in a dedicated visible row.
5. **Safety and tests:** Signed/sealed read-only LDAP, explicit GPO Preview, and offline User/Computer scope plus high-DPI layout checks. Incomplete OU group lists fall back to All groups with a warning.

**Limitations:** Direct membership lists do not expand nested groups or include primary groups. A GPO cannot be linked to a security group directly; link it to an approved OU, then apply security filtering. No new group/OU assignment bypasses AD ACL checks, scope confirmation or GPO preview. No automatic loopback or JPG photo User GPO deployment. Real Windows 10/11 200% display verification (#36), SMB LocalSystem pilot (#30) and JPG Screen Saver domain acceptance (#25/#26) remain open.

[Guide: OU and security group targeting](../OU-AND-SECURITY-GROUP-TARGETING.md) · [Changelog](../CHANGELOG.md)
