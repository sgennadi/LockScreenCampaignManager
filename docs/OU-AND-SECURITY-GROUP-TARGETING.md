## Preview.109 — remote-session timeouts and same-setting source overlaps

**Scan all DC GPOs** additionally detects RDS idle/active/disconnected session-time limit policy values (User or Computer policy half, including registry preferences). RDS time limits are recorded in milliseconds. Only RDS **MaxIdleTime** can be cautiously compared with the campaign's seconds-based idle Screen Saver threshold, and it is applicable to Remote Desktop sessions, **not proof of a local console Screen Saver blocker**. Total connection age, post-disconnect limits and whether to end instead of disconnect are separate remote-session conditions.

Choose **Compare GPO settings...** from the existing domain-wide audit window to identify the same User/Computer registry key and value name configured by more than one distinct GPO. The report distinguishes identical configured values, differing values, and contradictory source values within one GPO. It lists GPO names/IDs, how many are linked within any selected OU ancestry, and whether the evidence is only a domain-wide collision. No implicit link-order winner is calculated: settings can be linked on different target branches or filtered by security/WMI/GPP item-level targeting. Use gpresult/RSoP for the actual user/device session before changing any GPO.

This remains a **source audit**, not complete arbitrary-CSE settings coverage, not validation of interactive Desktop HKCU/MDM/Remote Desktop state, and not proof of simultaneous application. Exports stay local, require confirmation and guard against spreadsheet formula injection. No automatic remediation or remote actions occur.

## Preview.108 — scan all GPO objects on the selected DC

GPO Operations → Lock Screen conflicts → **Scan all DC GPOs** is an explicit read-only domain inventory, independent of the ordinary **Scan OU + GPO**. It uses the verified connected DC for signed/sealed, paged LDAP groupPolicyContainer enumeration (up to 2,000 GPCs), then inspects known source files in User and Computer policy halves under that *same* DC's SYSVOL. It does not launch scripts, write policies or run remote processes.

OU selections are optional for the full inventory; without a selected User/Computer OU, no policy is promoted to a linked candidate. The result separates: **Linked candidate** (selected OU or ancestor link, not proof of application), **Outside selected OUs**, **Inactive/filtered** (disabled link/half or blocked inheritance), and **User OU not verified** (when no User OU was selected). It flags missing GPC links, missing advertised Registry.pol, mismatched AD/SYSVOL halves, source access or parse failure as issues. It detects known Screen Saver HKCU values, desktop lock-screen policies, VIDEOIDLE/sleep AC/DC, machine inactivity and loopback UserPolicyMode, plus relevant GPP registry/power options.

This is a **domain-wide inventory of GPO objects**, **not** a complete semantic index of all possible GPO settings/extensions. Unsupported CSEs, scripts, security templates, sites, GPP item-level targeting, ACL/WMI filtering, user-device membership and winning GPO must be checked independently. A policy that exists outside a selected OU is only a domain inventory hit; none of the static rows proves that it applied to a client. No clean result is asserted from incomplete reads. Offline CI only verifies the parser, scope and read-only contracts; domain Windows acceptance is a separate gate.

## Preview.107 — verify a computer's observed power timings

In **GPO Operations → Lock Screen conflicts**, choose a Screen Saver or Both campaign, enter a domain **computer FQDN** belonging to the selected Computer OU, then click **Verify Screen Saver power (PC)**. This is an explicit diagnostic action: no PowerShell scripts, WinRM, remote process execution, tasks, policy edits or power-plan changes.

The manager first verifies the computer account is uniquely present, enabled and inside the selected OU on the selected DC using **signed/sealed LDAP**. Only then does it read Windows active-plan **display and sleep AC/DC indices** via WMI (Win32_PowerPlan / Win32_PowerSettingDataIndex / Win32_PowerSetting relationships) and HKLM computer policy values via read-only Remote Registry. A separate static OU/ancestor GPO scan shows candidate source GPO names and links.

The dialog compares **each evidence source individually** against the **campaign-configured** User Screen Saver idle interval. A timeout below that interval may block visibility; the same threshold is uncertain; zero in power timers means *Never*. Unknown/denied/ambiguous readings show **Not verified**. Remote HKLM values and active WMI plan values may disagree: neither is automatically promoted to the winning GPO. The report deliberately does not claim observed screen off, actual applied User HKCU, effective user/group targeting, live PC status or visual activation.

For acceptance, manually verify **computer and user gpresult**, actual applied Screen Saver policy in the signed-in user's HKCU, active powercfg /query, AC/DC behavior, MDM and modern standby, on a lab Windows 10/11 device. These requirements cannot be satisfied by GitHub-hosted CI.

## Preview.106 — Screen Saver and device power conflicts

A Screen Saver GPO targets **User OU + User Group**. A Computer OU can now additionally be selected on the Group assignments page for **device-side power GPO auditing only**; this does **not** create/link a Computer GPO, alter the User GPO scope, enable loopback or change group memberships. For **Both**, the existing Computer OU also supplies this audit context.

The Preview scans candidate *linked Computer GPOs* under that device OU and its ancestors, looking for **Turn off the display (AC/DC)** (`VIDEOIDLE`), **Sleep (AC/DC)**, **Machine inactivity limit**, and Group Policy Preferences Power Options. It separately scans the User OU's screen-saver settings. An earlier positive display/sleep timeout than the chosen User Screen Saver timeout is flagged as **high-risk candidate**; an equal value is a **warning**. `0` means Never for these power timers. The full report exposes the GPO name, key, configured timeout and source. If a Computer OU was not selected, the report explicitly says **Not verified** instead of claiming no conflict.

Supported static sources are linked GPO `Machine/Registry.pol`, `Machine/Preferences/Registry/Registry.xml` and `Machine/Preferences/PowerOptions/PowerOptions.xml`, plus User policy overlap. The GPP `GlobalPowerOptionsV2` numeric timers are seconds; legacy `PowerScheme` monitor timers are minutes. All GPP power settings are **candidate-only** because the active power scheme, item-level targeting, newer power states and client context have not been resolved.

Actual user-to-device association, winning GPOs, local/site/MDM policies, nested group ACLs and active power state require an endpoint **User + Computer gpresult and powercfg /query**. A selected Computer OU may not match where affected users actually sign in, so a static clean result never proves the Screen Saver will be displayed. No GPO is modified by these scans.

## Preview.106 — Client Health group coverage versus agent reports

Client Health uses the native read-only **Inventory** operation to verify the exact OU + nested Computer security-group intersection and selected DC time. The dashboard counts all computer accounts in the OU, the group intersection, enabled/disabled/unknown states and the enabled computers with approximate `lastLogonTimestamp` within a selected 30/90/180/365-day window. In a separate line it compares that group against actual received, fresh, stale or missing SMB agent JSON. A verified empty group remains **0**, not a collection of unrelated SMB reports; an incomplete/stale AD inventory or mismatched campaign is **Not verified**. Agent JSON is untrusted diagnostic telemetry, not online proof or authenticated identity. Changing the window is display-only and never filters deployment or Repair authorization.

## Preview.105 — approximate computer account activity (read-only)

For **Computer OU + Computer Group** only, use the **Last 30 / 90 / 180 / 365 days** selector in AD Deployment or Group assignments. The total count stays unchanged; a separate activity line shows **enabled**, **disabled**, **unknown account status**, **no valid logon timestamp**, and the number of **enabled** computers with a recent replicated `lastLogonTimestamp`. If a computer security group is selected, its **direct** member count within the same age window is also shown.

The cutoff uses the connected AD DC's RootDSE UTC rather than the manager PC's local time; if a trustworthy DC timestamp is unavailable, the activity estimate is **Not verified**. Disabled and unknown-state computers are not included in recent totals, even if a logon timestamp exists. A missing or malformed timestamp is not interpreted as a confirmed old/offline computer.

**This is an estimate, not a true online/last logon status.** `lastLogonTimestamp` is replicated with delays, often many days, and may lag actual activity; it is not `lastLogon` (per DC), a live heartbeat, a client JSON report, or a GPO applicability verdict. Switching time windows is **display-only** and does not filter the members actually receiving a GPO, change the selected AD group, erase stale accounts, change User Screen Saver scope or change Client Health's expected computer list.

# OU and security group targeting — Preview.100

## Preview.104 — verified direct coverage (read-only)

The AD Deployment window shows the number of **computer accounts in the selected OU subtree**, and how many are **direct members of the selected computer security group**. The Group assignments window shows separate counters for **Computer OU + Computer Group** and **User OU + User Group**.

A verified LDAP response distinguishes **0 direct members** from **Not verified**. The latter appears on initial selection, after changing OU/DC or if the membership result is incomplete or fails. Counts are generated by signed/sealed LDAP without sending names of users or computers to the UI. Selecting **All groups** keeps the counter available without changing the selected group.

**Not a deployment estimate:** these totals include disabled and stale AD accounts. Direct `memberOf` does not expand nested groups or include primary-group membership. It is not equivalent to effective security filtering, permission to Apply Group Policy, `gpresult`, a successful client report or an online computer count. For real rollout, separately filter inactive computers and verify a small pilot OU.


**A security group is a filter, not a GPO link.** Group Policy must be linked to an OU (or domain/site). A security group can reside elsewhere in Active Directory; its members can be within the linked OU.

## In the AD Deployment Wizard

1. **Select an OU → Groups with computers in selected OU (direct).** The app reads computer accounts in the selected OU and descendant OUs with signed/sealed LDAP and shows only security groups listed in those computers' direct memberOf memberships. The OU of the group object does not matter.
2. **Choose a group first → All domain security groups.** Select the security group, then select the explicit OU to LINK the GPO. Choosing a group alone never authorizes deployment. You can switch to All groups at any time to include nested/primary groups not listed by the direct view.
3. **Same short OU names** display their parent OU paths; exact duplicate DNs are removed, but the full DN remains the actual identity.
4. **Screen Saver → User OU / groups...** uses User Configuration (HKCU) with a USER OU/group. The group matrix has an optional direct-user-membership filter that can be turned off to browse all groups. Lock Screen uses Computer Configuration (HKLM) and COMPUTER groups. Both requires two independent target pairs.
5. **Browse...** for SMB content is in a separate full-width folder row, visible without scrolling horizontally. It is enabled only after selecting SMB network folder (UNC).

## Limits and safeguards

Direct memberOf queries DO NOT expand nested groups or include primary groups. An OU lookup over 5,000 objects or a failed LDAP query does not produce a misleading partial result: the UI switches back to All groups with a warning. OU and group filtering is for browsing only, not proof that a GPO applies. Read-only Preview and a real Windows gpresult/RSoP check must account for nested membership, denies, WMI, inheritance and loopback.

For Screen Saver applied to users on particular computers, Group Policy loopback requires explicit separate design and approval; the app does not enable it automatically. Automatic JPG Photo Screen Saver User GPO deployment remains blocked pending real Windows 10/11 AD acceptance (#25/#26).

No domain change is made by searching/filtering. The existing separate Preview/Deploy confirmation, GPO scope verification, identity checks, rollback requirements and safeguards are unchanged. Physical DPI 200% and real domain filter acceptance require a test workstation and are not proven by hosted GitHub Actions.
