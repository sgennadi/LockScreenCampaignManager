# LockScreen Campaign Manager — 0.4.0-preview.117

## What's new

1. **Loopback Replace is not treated as verified.** When a selected DC detects a possible Replace mode, ordinary User OU GPO rows are marked conditional rather than "AD candidate". Possible Merge and unknown modes also keep computer-OU User GPOs conditional until endpoint evidence is available. Disabled links, blocked inheritance and explicit security denies stay excluded.
2. **GPO flags are fail-closed.** The AD-only scope analyzer accepts the normal absent `flags` attribute as zero (both halves enabled), but a non-numeric or unsupported GPC flag no longer silently becomes zero. Such policies are reported unreadable/unknown rather than active.
3. **Stale, partial and computer-only scope stays unverified.** A stale/incomplete AD-only scan cannot stand in for missing Computer/User gpresult data. A PC-only scope cannot verify a User account's GPO applicability. An incomplete all-GPO source scan also prevents a current AD-applicability status. Loopback status remains unverified if its evidence is stale or partial.
4. **Preserve stronger manually imported evidence.** A current, operator-imported `gpresult` report identifying a GPO as applied/filtered is no longer overwritten by a weaker AD-only candidate. When a User XML is combined with an AD user scope, its principal must match the selected AD account name (exact UPN, or matching SAM in a DOMAIN\\sam form); otherwise the XML is flagged as uncorrelated and cannot elevate priority. Matching strings do **not** authenticate the XML, determine the winning registry setting or establish actual endpoint policy application.
5. **Regression and translations.** New native self-tests cover GPC flags, Replace/Merge/unknown loopback, partial and expired snapshots, computer-only scope and XML+AD correlation. Existing Windows x86/x64 packaging, 100/150/200% DPI and EN/RU/HE checks remain; additional Russian/Hebrew labels explain conditional loopback states.

## How to verify

Open **GPO Operations → Conflicts → Scan all DC GPOs → Analyze AD scope (no XML)...** and choose the exact Windows computer FQDN in the scanned OU. Enter the real test user's SAM/UPN when inspecting User Configuration. Use **Review GPO blockers...** to compare the GPO GUID, setting and link origin. A detected possible Replace should mark User OU source rows **Conditional: loopback Replace candidate** and computer-OU User rows **Conditional: loopback not confirmed**. A missing user must never make User GPO evidence verified.

Optionally import separately captured Computer/User `gpresult /x` files to add signed-in-session historical evidence, then perform a real Windows 10/11 pilot to verify RSoP, HKCU and active screensaver behavior. No XML import is required for the AD-only scan.

## Safety and open limits

All new scanning operations are read-only. They make no AD, GPO, SYSVOL or endpoint changes, and they never trigger gpupdate or automatic repair. Site-linked GPOs, WMI filter results, GPP item-level targeting, CSE processing, multi-DC replication, computer Read permissions when processing User GPOs, live policy precedence and actual screen/lock behavior remain unverified. Test on a real domain computer before any production deployment.
