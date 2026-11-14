# LockScreen Campaign Manager — 0.4.0-preview.119

## What's new

1. **Missing group tokens never count as a complete AD scan.** The no-XML pilot requires verified tokenGroups for the selected computer, plus those of the selected user when User Configuration is inspected. An empty GPO list or exclusively excluded links cannot hide a missing principal token.
2. **Explicit computer GPC Read Deny remains visible.** If a User GPO's selected computer is definitively denied Read, that GPO source is excluded even when the selected user's Read+Apply membership is unresolved. Previously excluded links and policy halves keep their original cause.
3. **Strict AD-only evidence.** A scoped snapshot may not claim complete GPO applicability if a required token is unavailable. A GPO cannot be labeled an AD candidate without verified Read+Apply rights, the necessary computer token and computer GPC Read (for User GPOs).
4. **Regression coverage.** New native tests cover missing Computer/User tokenGroups, independent exclusions and Deny priority. Existing Windows x64/x86, offline AD lab, English/Russian/Hebrew and simulated 100–200% DPI checks remain CI gates.

## Verification

In **GPO Operations → Conflicts → Scan all DC GPOs → Analyze AD scope (no XML)...**, select the exact test computer FQDN and its OU, then the exact test user's SAM name or UPN. Inspect **Pilot Readiness → AD applicability**, **AD scope freshness**, and **Computer GPO Read**. Use **Review GPO blockers...** to inspect policy GUIDs, source links and Read/Deny/Unknown evidence.

When the selected computer or user tokenGroups are unavailable, the GPO applicability result must not be marked complete even if every discovered GPO is excluded. An explicit computer Read Deny is a source-side explanation, not proof of actual endpoint state.

## Safety and outstanding limits

This is a read-only LDAP GPC/OU security analysis against the selected DC. It does not verify the endpoint's live token, SYSVOL SMB/share/NTFS permissions, GPP item targeting, site/WMI results, CSE processing, precedence, winning User policy or visible Screen Saver behavior. No AD/GPO/SYSVOL/endpoint writes, gpupdate or automatic remediation. Real isolated domain-connected Windows 10/11 pilot acceptance is required before production deployment.
