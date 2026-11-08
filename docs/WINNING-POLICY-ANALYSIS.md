# Winning Policy Analysis — 0.4.0-preview.25

This is a **read-only, per-registry-setting priority estimate**, NOT a statement
that a particular GPO currently controls the Windows lock screen. Windows 11
domain acceptance is deferred; the normal GitHub-hosted workflow builds and
tests synthetic fixtures only.

## Open the analysis

1. In **GPO Operations → GPO: Disable / Reuse / Delete**, choose the managed
   campaign GPO and its OU + computer security group.
2. In **Lock Screen conflicts**, enter the domain computer **FQDN**.
3. Select **Analyze effective (PC)**. The selected DC must verify the actual AD
   computer and its group membership. No cross-domain or unverified computer
   is accepted.
4. Optionally select **Import gpresult XML** from a *local* computer-scope
   gpresult /scope computer /x snapshot. Importing does **not** fetch a new
   report, run client commands or authenticate the origin of the XML.
5. Select **Winning policy analysis** to see each statically declared lock-screen
   registry setting, candidate source GPOs, GPMC Link Order, OU depth, Enforced
   flag, source value and client-level snapshot observation (if imported).
6. Filter **Conflicting values**, **Ranked leaders**, **Unranked**, or search;
   **Export visible CSV** requires an explicit privacy confirmation.

The prior **Scan OU + GPO** feature and effective candidate report remain.

## GPO precedence

For GPO settings with the same registry key/value name:

- The analyzer reads **GPMC Link Order** from gPLink correctly: within a
  container, Link Order **1** has the highest priority (the rightmost LDAP entry).
- The scanner follows the **actual verified computer's container** and parent
  OU/domain hierarchy, including child OUs below the scope OU supplied by the
  administrator. It does not stop early at the selected search OU.
- Without Enforced, deeper OU settings outrank ancestor OU/domain settings.
  An ancestor Enforced GPO can prevent a lower-level GPO from overriding
  that setting. Within one container link order remains decisive.
- Site-linked GPOs cannot be reliably ranked without the actual client site;
  they remain explicitly unverified. A disabled or blocked link is excluded
  from the ranking, but retained in the source list.

The UI labels a result **Static priority leader (unverified)** or **Static
setting source (unverified)**. It will **never** label it "Verified winner".
If GPP Registry Preferences are involved, the setting is **Order uncertain**:
GPP operations, item-level targeting and deletion/replace semantics cannot be
deduced from the registry value shown in a static scan. Ties and missing/unsafe
link information are also unranked.

## Important limits

Even with an imported XML gpresult report, this analysis only has **GPO-level
reported applied/filtered flags**. Such flags cannot prove which policy's
**individual registry setting** won. The XML is unsigned, point-in-time and
potentially historical; no client collection or mutation is performed.

A managed LockScreen campaign uses a **GPO startup script**, and scripts can
write the registry outside the static Registry.pol setting engine. Other
startup scripts, security/WMI filtering, local registry, Group Policy client
extensions, GPP item-level targeting, DC replication lag, unknown Site policies,
and Windows device configuration can override the observed image. An installed
agent and the actual displayed lock screen are independent observations.

Consequently **no static priority order is proof that the campaign image is
displayed**. Verify on an authorized Windows 11 computer with RSoP and
independent read-only endpoint observations before acting on conflicts.

## Security and testing

- The PowerShell scanner uses signed/sealed LDAP to the chosen DC and read-only
  SYSVOL access. No RSAT, WinRM, automatic GPO writes or remote scripts.
- The analysis window never changes registry values, security groups, GPO links,
  scheduled tasks, or user-selected campaign settings.
- Per-setting CSV may contain sensitive internal hostnames, GPO GUIDs, paths and
  registry values. It is manual, local and escaped against spreadsheet formulas.
  Raw XML is not uploaded to GitHub.
- CI exercises pure GPMC link ordering, C# precedence and "never verified"
  safeguards, plus WinForms UI/DPI smoke on GitHub Windows x86/x64.
  CI does **not** connect to an actual DC or Windows 11 lab computer.
- Stable release unchanged. Application, bundled agent and updater remain
  together on **0.4.0-preview.25**.

## Official Windows precedence references

- [Microsoft Learn — Group Policy processing](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/group-policy/group-policy-processing)
- [Microsoft Learn — Group Policy scope](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/group-policy/group-policy-scope)
- [Microsoft Learn — Set-GPLink: Order](https://learn.microsoft.com/en-us/powershell/module/grouppolicy/set-gplink)
