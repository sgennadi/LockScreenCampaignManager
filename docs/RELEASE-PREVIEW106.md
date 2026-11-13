# LockScreen Campaign Manager — 0.4.0-preview.106

## What's new

1. **Screen Saver vs power GPO:** Find candidate linked Computer GPO policies that turn off the display before a configured User Screen Saver starts, comparing AC (plugged in) and DC (battery) timeouts in seconds; zero means **Never**, not immediate display off.
2. **Power-policy sources:** Read only Computer **Registry.pol**, GPP **Registry.xml**, and GPP **PowerOptions.xml** from the selected DC's scoped OU/ancestor GPOs. Surface sleep and machine inactivity/lock settings; show the originating policy, value and GPO source.
3. **Separate User/Computer scopes:** In Group assignments, choose an **optional Computer OU for Screen Saver power auditing**. It does not create a Computer GPO, alter user targeting, enable loopback or change security-group membership. An unknown device OU creates a visible **Not verified** warning.
4. **Client Health AD vs agents:** Use read-only verified Computer OU/group inventory to distinguish all OU computer accounts, exact nested-group members, enabled/disabled/unknown accounts, approximate AD logons (30/90/180/365 days), fresh/stale SMB agent reports and missing client reports, with an explicit difference between verified zero and not loaded.
5. **Safety, languages and tests:** Add English/Русский/עברית coverage labels, six-profile simulated DPI smoke, offline GPO and feedback validation tests and native x64/x86 CI. Continue to require independent GPO Preview, permissions review and deployment approval.

**Read-only interpretation and limitations:** These checks are *static candidates*, **NOT effective gpresult/RSoP**. Actual Group Policy precedence, site policies, inherited ACL/security/WMI filtering, GPP item-level targeting, active power plan, power states, connected standby, MDM/Intune overrides and exact user-to-device associations may change results. A Computer OU chosen for audit may not be the OU of the computers where screened users sign in. Validate a pilot client with user + computer `gpresult`, `powercfg /query`, and the actual .scr. No GPO, SYSVOL or client power settings are changed by the audit.

AD `lastLogonTimestamp` is replicated approximately and can lag; it is not a live heartbeat or authentication. SMB JSON is not authenticated device attestation. The 30/90/180/365-day selector changes only displayed estimates: it never filters actual GPO/Repair targets or Client Health expected scope. Verified AD inventories expire for comparison; stale or incomplete evidence is never reported as verified zero.

Real Windows 10/11 domain acceptance, JPG user GPO pilot, LocalSystem SMB and physical 200% DPI/RTL checks remain open.

[OU/group targeting guide](OU-AND-SECURITY-GROUP-TARGETING.md) · [Changelog](../CHANGELOG.md)
