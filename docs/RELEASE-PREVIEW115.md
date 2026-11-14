# LockScreen Campaign Manager — 0.4.0-preview.115

## What's new

1. **One unified GPO analyzer.** From **GPO Operations → Conflicts → Scan all DC GPOs**, select **Analyze AD scope (no XML)...**. The existing `gpresult` comparison remains optional evidence for the same Pilot Readiness screen.
2. **Exact AD identities.** Enter a pilot **Computer FQDN** and scanned Computer OU, and optionally an exact **User sAMAccountName or UPN**. The selected domain controller verifies object existence and OU containment. No need to upload a `gpresult` XML.
3. **Security and inheritance.** The read-only scan uses `tokenGroups` (nested and primary-group SID membership), GPO `nTSecurityDescriptor` **Read + Apply Group Policy** permissions and matching **Deny** ACEs, OU-to-domain GPO links, **Block Inheritance**, **Enforced**, link-disabled and disabled User/Computer GPO halves. Missing ACL or token data remains **Unknown**.
4. **Loopback.** The same scanner examines Machine `Registry.pol` `UserPolicyMode` and reports **Merge candidate**, **Replace candidate**, **Not configured**, or **Unknown**. When loopback may apply, it evaluates User halves of computer-OU GPOs against the *user* ACL. A Replace candidate does NOT suppress User OU GPOs without verified precedence.
5. **Screen Saver blockers without XML.** The single Pilot Readiness report combines source GPO GUID, scope, registry setting/value and AD applicability. Optional `gpresult` XML, local HKCU JSON and remote power observation add evidence but are not prerequisites. EN/RU/HE labels and high-DPI UI tests are included.

## Pilot procedure

1. Connect to an explicitly selected DC with read access to AD, GPO ACLs and the controller's SYSVOL. No AD write permission is required.
2. Select the relevant Computer/User OUs in the campaign and run **Scan all DC GPOs**. Click **Analyze AD scope (no XML)...** directly — there is no required XML import step.
3. Enter a Computer FQDN within the scanned Computer OU. Enter a User SAM or UPN to analyze User Configuration, nested groups and loopback; leave blank for computer-only checking.
4. Click **Analyze AD scope (no XML)...** inside Pilot Readiness. Inspect **AD applicability**, **AD loopback**, GPO candidates/exclusions, group tokens, ACL and **Review GPO blockers...**.
5. Optionally compare the two `gpresult /x` files and a locally captured HKCU JSON, then check Screen Saver idle/display/lock activation on real Windows 10 and Windows 11 pilot computers.

## Confidence and safety limits

- **AD scope is not client RSoP.** Links, Enforced/Block Inheritance, group membership and ACL are useful to establish *expected AD applicability*, never a confirmed final Screen Saver setting. XML is not needed for these directory-side checks.
- **One user is not every user in a group/OU.** Deny ACEs, nested group membership, WMI and GPP targeting can vary by principal and endpoint.
- Site GPO links/site assignment, actual WMI filter results, GPP item-level targeting, computer Read permissions for user policy, CSE processing, local/MDM policies, replication, endpoint sessions and real Screen Saver display remain **unverified** by this AD-only scan.
- Loopback Merge/Replace is a source candidate rather than a verified winning GPO. Ambiguous results retain both possible User OU and Computer OU paths.
- **No domain writes, scripts, gpupdate, remote commands, or automatic remediation.** Automatic custom SCR/EXE/JPG User GPO deployment remains blocked pending real Windows/AD acceptance.

**CI boundary:** Hosted Windows x64/x86 builds, offline contract/self-tests and package checks do not replace physical Windows 10/11 and real-domain pilot validation.
