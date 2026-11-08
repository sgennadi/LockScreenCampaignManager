# Effective GPO Conflict Analysis — 0.4.0-preview.23

**Scope:** Conservative, read-only examination of **one selected AD computer** and managed LockScreen campaign on the selected DC. This is **NOT** a Windows Resultant Set of Policy (RSoP) engine. No live DC/Windows 11 test has been performed; GitHub Actions only checks isolated and synthetic fixtures.

## Where to open

In **GPO Operations**, choose a managed campaign GPO on the **GPO: Disable / Reuse / Delete** tab. Open **Lock Screen conflicts** and enter the full DNS computer name, for example `PC022.example.com`.

- **Scan OU + GPO:** the existing, unchanged candidate scan (OU chain, direct/inherited links, `Registry.pol`, GPP XML and optional remote HKLM).
- **Analyze effective (PC):** the new read-only AD-scoped analysis. It requires the computer to be found uniquely within the selected OU subtree and to be an **effective direct/nested member** (or primary-group member) of the selected computer security group on the selected DC. Invalid/foreign FQDNs, missing computer accounts, missing group membership, cross-domain GPOs and a non-managed selected GPO are rejected.

A new English-only DPI-aware report lists each GPO, assessment, link state, security ACL hint, WMI presence, selected registry settings and notes. You can filter policies, search for an individual setting and manually confirm a local CSV export. Neither mode changes AD/GPO, remotely executes PowerShell or schedules a task.

## Interpretation

The scanner reads the domain, OU-ancestor chain and selected computer account through signed/sealed LDAP, with explicit DC binding. For computer-targeted GPO candidates it considers:

- **OU inheritance:** direct and ancestor `gPLink`, disabled links, Enforced flags, `gPOptions` Block Inheritance and disabled GPO computer configuration.
- **Security Filtering:** the computer's `tokenGroups` from the selected DC and matching GPO DACL Read / Apply Group Policy allow/deny ACE hints. This is NOT the fully evaluated access check: group token composition, DACL inheritance, other trustee combinations and file permissions can affect actual application. An unavailable token or ACL is **Unknown**, never a confirmed allow or denial.
- **WMI:** `gPCWQLFilter` is checked only for **presence**. The filter is not executed on the client and its result is **Unknown**.
- **Site GPO:** the named DC's configuration partition is inspected for potential Site-linked policies. The client computer's actual site is **Unknown**; all readable site links are possible candidates, **not** evidence that the site's GPO applies. If the site inventory is incomplete, the report prominently warns that site coverage is unknown.
- **Registry overlaps:** reuses the existing safe, non-executing parser for `Machine\Registry.pol` and `Machine\Preferences\Registry\Registry.xml` to flag LockScreenImage, NoChangingLockScreen, NoLockScreen and related CloudContent/Windows Spotlight settings. Startup scripts are treated as dynamic unknowns: they are **never executed**, and their actual registry effects cannot be inferred.

### Status meanings

- **Excluded by link/config:** link is disabled, computer settings are disabled, or non-Enforced ancestry is blocked. Other site/local policies may still exist.
- **Security deny candidate:** an ACL deny hint was observed, but a full client access check was not performed.
- **Needs RSoP / verification:** token, ACL, WMI, XML/settings read, or startup-script effects remain uncertain.
- **Site applicability unknown:** potential site-linked GPO; target computer site not established.
- **Potentially applicable:** OU/link, computer-setting and basic ACL hints are compatible, with no WMI or detected script uncertainty; **does not prove a policy applied or that its setting won precedence**.

The potential-high-overlap count is limited to GPOs in the last category that have a high-priority static LockScreen registry setting. It is **not** a fleet conflict count, not a proven effective value and not a safe basis for overriding another GPO.

## What requires the actual computer

Windows policy precedence and Group Policy client-side extensions, **GPP item-level targeting**, security token/access checks, WMI result, Site membership, local registry policies and RSoP must be inspected on the target computer. On an authorized Windows 11 client, an administrator can use `gpresult /h <local-report-path>` or the Group Policy operational event log. An inaccessible PC is **unknown**, never automatically cleared.

The scanner does **not** perform `gpupdate`, modify security filtering, grant group membership, change GPO links/registry values, fix replication, enable RemoteRegistry, retry RPC or upload the analysis to GitHub.

## Implementation / CI

The existing bundled `Gpo-ConflictScan.ps1` now supports a separate `Effective` mode, leaving `Scan` available. The response is strictly bound to domain/DC/OU/group/computer/managed GPO/campaign and checked again by the C# model. Untrusted responses, impossible "applied" site claims, optimistic token/ACL classification, duplicate/foreign GPO IDs and mismatched scope are rejected rather than displayed as success.

GitHub Windows x86/x64 CI parses all PowerShell with version 5.1, runs independent pure helper tests for DNS/LDAP escaping and inheritance, checks that the scanner contains no mutating AD commands, runs model SelfTests for site/ACL uncertainty, and opens the report through WinForms DPI smoke tests. Hosted runners do not validate actual GPO application in your domain.

**Version:** 0.4.0-preview.23. Stable branch/release is unchanged.
