# Universal edition migration and publication

This guide applies to the vendor-neutral source candidate `0.4.0-preview.29`.
The current branch is **not** a verified production release. It has no live
domain-controller or Windows 11 deployment test.

## Identity and local state

The GUI assembly and executable are now `LockScreenCampaignManager.exe`.
The application writes its own new profile and settings data rather than
implicitly adopting another application's stored company connections.
The campaign agent is now distributed as `LockScreen-Agent.ps1`; new campaigns
use the `LockScreenCampaigns` client-data directory and new task identities.

**The name change is not an in-place upgrade of installed endpoint agents, GPOs,
scheduled tasks or source packages.** Do not rewrite the agent filename inside
an existing campaign package: its expected SHA-256 manifest would no longer match.
Do not copy previous unreviewed profile files into a public repository.

## Domain migration: safe sequence

1. Inspect the *actual* target OU and all linked Lock Screen policies. Identify
   active campaigns, their client tasks, scheduled end times and original
   registry backup ownership.
2. Let active campaigns finish and verify client cleanup and restoration. If
   closure is incomplete, use the original trusted agent's supported Cleanup
   flow and explicitly confirm results; never delete the previous task or
   backup first.
3. Disable or unlink retired GPOs only with domain administrator approval.
   Keep a backup of existing policy and deployment metadata. Avoid deleting
   SYSVOL source while any old client still needs it for repair or cleanup.
4. Open the new manager. Begin with **1 Connect to DC & Read AD**, confirm the
   DNS domain and selected DC, then examine OUs, groups and GPO conflicts.
5. Create a **new** campaign package with a **new** campaign ID. Use a distinct,
   reviewed GPO name and appropriate computer security group. Check scheduled
   image dates, time zone and hashes.
6. Test the new package with a small Windows 11 lab OU and verify both install
   and cleanup. Only then consider a wider rollout.

The new endpoint agent is fail-closed if it detects a foreign Lock Screen owner
or another image path in ProgramData. Do not bypass that guard to force migration.

## Publication safety

The historical private repository must remain private even if its current
source tree has been scrubbed. Git commit history, tags, branches, old Releases,
issue content and workflow artifacts are separate disclosure surfaces.
Renaming that repository does not remove them.

For public distribution, create a **new empty repository** named
`sgennadi/LockScreenCampaignManager` (not a fork). After independent review,
import **only the current clean source snapshot** using a fresh initial commit;
do not import previous Git objects, branches, release ZIPs or logs.
Verify builds, offline tests, updated diagnostics/updater release destinations,
and any signing process before publishing a preview.

## Verification boundaries

Repository source checks cannot prove the absence of all sensitive data,
correctness of deployment, functioning time-zone rules on endpoints, or a visible
Lock Screen image. The GitHub-hosted Windows jobs have not reached their first
step in recent runs; no x86/x64 executable build from this branch is verified.
