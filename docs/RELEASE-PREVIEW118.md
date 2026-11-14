# LockScreen Campaign Manager — 0.4.0-preview.118

## What's new

1. **Computer GPO Read is checked for User policies.** The same AD-only, no-XML analyzer uses the selected computer's verified AD token groups to inspect the User GPO container's **Read** permission in addition to the selected user's existing **Read + Apply Group Policy** checks. This addresses the computer-context User GPO retrieval requirement introduced by MS16-072. No user or computer membership is assumed.
2. **No false application when computer access is missing.** A matching computer **Deny Read** on a User GPO is classified **Excluded: computer GPO Read denied** (source evidence). An unavailable computer token, unreadable ACL or missing verified Read grant is **Conditional: computer Read not verified**. Previously excluded links/halves/ACL denies remain excluded.
3. **One report with visible cause.** Pilot Readiness now includes **Computer GPO Read** totals for allowed, denied, unknown and already excluded/unevaluated User GPO links. The linked GPO source details and named blocker interpretations include this computer-side permission alongside user ACL, WMI and the link's OU.
4. **Russian and Hebrew plus regression checks.** New statuses and the Computer Read diagnostic title are translated. Native self-tests exercise **Read / Deny / Unknown**, strict scoped evidence parsing, and the existing Loopback/identity logic. Previous WinForms DPI and all-domain read-only safety tests remain enabled.

## How to verify

Open **GPO Operations → Conflicts → Scan all DC GPOs → Analyze AD scope (no XML)...**. Select the Computer FQDN and its verified OU, enter the exact pilot AD User SAM/UPN and run the read-only scan. Inspect **Computer GPO Read** in Pilot Readiness, then **Review GPO blockers...** for GUID, User OU or loopback origin, user Read+Apply and computer GPC Read statuses.

For a dedicated nonproduction pilot, compare a User GPO that permits the selected computer (or an appropriate group) to Read with a User GPO whose GPC ACL explicitly denies that computer Read. A verified explicit deny should be highlighted as a potential source-side reason the User policy cannot load; unverified grants must **not** appear as successful application.

## Safety and limitations

This checks the selected DC's **GPC LDAP DACL Read** using the selected computer's AD tokenGroups only. It does not attest the endpoint's live security token, authenticated SYSVOL SMB/share/NTFS access, GPP targeting, site GPOs, WMI results, client-side extensions, user logon session, actual winning per-setting policy or visible Screen Saver behavior. No AD, GPO, SYSVOL or endpoint writes, remote scripts, gpupdate or automatic remediation. Real Windows 10/11 pilot acceptance is still required before production rollout.
