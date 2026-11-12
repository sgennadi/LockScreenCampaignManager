# LockScreen Campaign Manager — 0.4.0-preview.104

## What's new

1. **OU computers and users:** AD Deployment shows the number of computer objects in the selected OU and its child OUs; Group assignments shows independent computer and user totals.
2. **Selected security group coverage:** Each selected group displays how many objects from that OU belong to the group **directly**. Groups can be stored in another OU, and the group-first / All groups choice is preserved.
3. **No misleading zero:** An incomplete, stale or mismatched LDAP read shows **Not verified** rather than zero. A confirmed empty OU or zero direct members is shown only when the query successfully completed.
4. **English / Русский / עברית:** Count labels wrap at high DPI, keep numeric values intact and do not translate DN or policy identifiers.
5. **Safe read-only verification:** Signed/sealed, capped LDAP requests; independent User/Computer scope validation; offline tests and WinForms smoke checks. No AD/GPO write or deployment is triggered.

**Important:** Direct `memberOf` counts include disabled/inactive directory objects. They do **not** expand nested security groups, include primary-group membership, filter by last logon, resolve WMI, allow/deny ACLs or prove that a GPO actually applies. Verify that separately on a dedicated Windows 10/11 machine with `gpresult`/RSoP. Real 200% display acceptance (#36), SMB LocalSystem test (#30) and JPG Screen Saver User GPO pilot (#25/#26) remain open.

[OU and group targeting guide](OU-AND-SECURITY-GROUP-TARGETING.md) · [Changelog](../CHANGELOG.md)
