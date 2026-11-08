# Public release procedure — vendor-neutral source

The code in this branch is a **candidate**, not an independently tested release.

## Repository identity

- Target new repository: `sgennadi/LockScreenCampaignManager`
- Application binary: `LockScreenCampaignManager.exe`
- Project: `src/LockScreenCampaignManager/LockScreenCampaignManager.csproj`
- Current candidate: `0.4.0-preview.29`
- UI: English, BitKeyBridge-inspired navy/blue/green, responsive high-DPI layout
- Domain: discovered explicitly via Connect to DC & Read AD; no fixed company/DC/OU

## Why the existing private repository must NOT be made public

The original repository's object database contains earlier history, branch names,
release artifacts and metadata using organization-specific identifiers. A simple
rename, delete commit, or update to HEAD does not remove those records.

Use a **new empty public repository with fresh history**, and copy only an audited
source snapshot. Do not migrate old Git commits, tags, Releases, Issues, GitHub
Actions logs/artifacts, deployment files, downloaded client reports or settings.

## Publishing the vetted snapshot

Only after the source audit and Windows build pass, an administrator can run
these commands from a trusted computer with GitHub CLI authentication.
They do not edit the old private repository:

1. Clone the existing private repository, checking out only the dedicated
   universal branch.
2. Create an empty destination directory outside the clone.
3. Copy the tracked source files into that directory **without** its .git folder.
4. Run `python tools/audit_public_snapshot.py` in the snapshot after
   initializing a fresh Git repository, and review all output.
5. Check no `.env`, debug logs, report JSON, PowerShell transcripts, signing keys,
   customer image files or machine-specific settings were included.
6. Run `python tools/test_package.py` and the remaining repository tests.
   Build and self-test the Windows x86 and x64 EXEs on a trusted Windows machine.
7. Select a license deliberately. Do not assume public means open source.
8. Create a **new** public `sgennadi/LockScreenCampaignManager` repository.
9. Commit the clean snapshot as the first commit and push only this new branch.
10. Verify GitHub Actions tests, source privacy scan and downloadable assets.
    Only then publish the preview release with SHA-256 checks.

Do not merge or expose the old private repository history.

## Legacy deployments and client safeguards

A newly named application does not migrate, overwrite or silently remove a
previous agent or GPO. The new agent detects an existing LockScreenImage path
outside its own verified campaign directory and refuses to take over.

For an existing managed campaign, use the original manager to complete cleanup
and verify restoration before deploying the new GPO/agent. Do not rename an
active SYSVOL package: its existing agent SHA-256 and manifest are immutable.

Default campaign/company time zone follows the Windows administration
workstation, but the administrator can select any Windows time zone. The DC's
RootDSE UTC observation is useful for drift checks; it does not establish a
corporation's preferred civil time zone. Campaign scheduling remains explicit.

## Pending validation

- CI Windows runner acquisition and build of the new assembly.
- Portable EXE startup, --self-test and --ui-smoke-test on x64 and x86.
- Test of clean new AD domain and GPO lifecycle on isolated Windows 11 VMs.
- Legacy campaign conflict/cleanup and fresh-install paths.
- Public history, LICENSE, security policy, updater URLs and release assets.

No live DC/PC tests were performed by this source migration.
