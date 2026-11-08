# Offline AD + Windows 11 Lab Kit (no GitHub runner)

**No domain-joined runner is required.** GitHub Actions only assembles and checks a portable ZIP on Microsoft's Windows runners. You download the ZIP to a domain-joined Windows management workstation and perform the real AD/GPO/Windows 11 integration test **locally**. There is **no automatic upload** of credentials, computer lists, GPO IDs, client data or test reports.

This is a pre-release test kit. Start with **Preflight**. The **Full** test is disruptive (creates a new GPO, modifies SYSVOL, reboots disposable Windows 11 test PCs, then attempts cleanup). Never use production PCs or an ordinary user OU. The pilot is not complete until a real local run passes and its results are examined.

## Download and prepare

1. In GitHub open **Actions → Offline AD + Windows 11 Lab Kit (no runner)**. Open the successful **main** workflow run and download the artifact named **LSCM-Offline-AD-Win11-Lab-Kit**. It contains one ZIP named `LSCM-Offline-AD-Win11-Test-Kit-0.4.0-preview.N.zip`. Do not take an unreviewed PR build.
2. Extract the ZIP to a writable folder on a **domain-joined Windows 10/11 management workstation**. Do not use a domain controller as the operator machine.
3. Copy `lab.config.sample.json` to `lab.config.json` in the same root folder. Edit only the selected domain, writable DC, OU DN, group DN and 1–3 test PC names. No passwords or GitHub tokens are needed.
4. Use a **new, isolated test OU** whose first RDN begins `OU=LSCM-CI`; put **only disposable Windows 11 Enterprise/Education computers** there, with no extra objects or nested OUs. Use a dedicated security group whose first RDN begins `CN=LSCM-CI`, containing *exactly* those computers as direct members. These objects must already exist; the kit never silently creates a test OU or adds PCs to a group.
5. Sign in with an authorized domain service/admin identity that has delegated GPO creation/link/delete rights for this test OU, selected DC SYSVOL permissions, and administrator remote Task Scheduler / `C$` SMB access on the selected test PCs. Windows Time must be synchronized; source image policy must not already be owned by an older campaign.
6. From an elevated PowerShell window, run `./Run-Preflight.cmd` or double-click **Run-Preflight.cmd**. This reads the selected DC's `RootDSE.currentTime`, checks OU/group isolation and inherited GPO conflicts, and briefly creates a temporary SYSTEM task plus probe files on the *selected test Windows 11 machines*. Preflight **does not create a GPO, alter lock-screen policy or reboot PCs**. Read the locally generated `reports/lscm-ci-*/live-ad-lab.json` for results.
7. Only after reviewing a **successful** Preflight, and **within 60 minutes on the same machine and unchanged configuration**, run **Run-Full-Lab.cmd**, and type `I_APPROVE_AD_LAB_TEST` when asked. Full repeats validation, uses the existing application GPO engine to create a unique test campaign and GPO, reboots selected PCs, verifies real startup/agent/image changes and the second scheduled image, then unlinks/deletes the test GPO and its exact source folder **only if** client cleanup is confirmed. Any unverified cleanup produces a failed report and may require manual action.

Recommended first pilot: **one disposable Windows 11 virtual machine**. Enable additional machines only after one machine works end to end.

## Example for ad.example.test

The JSON supplied in the kit deliberately uses `example.com`, and is **not** an approved live configuration. For a domain named `ad.example.test`, the DC name might be `example-dc01.ad.example.test`; confirm the actual DNS hostname in AD. Example *new test-only* identities:

- `OU=LSCM-CI,OU=Test,DC=example,DC=ac,DC=il` (must actually exist).
- `CN=LSCM-CI-Computers,OU=Test,DC=example,DC=ac,DC=il` (must actually exist).
- `WIN11CI01` (replace with the actual computer name of your disposable test VM).

Do **not** copy a real production OU DN into the configuration. The harness requires the `LSCM-CI` naming convention and verifies OU contents and exact security group membership before attempting deployment.

## What is actually tested

Preflight: writable selected DC, UTC clock and drift; OU and security group membership; inherited lockscreen GPO conflicts; Windows 11 Enterprise/Education; Windows Time source; absence of existing lockscreen campaign/ownership.

Full: two distinct generated JPEGs with SHA-256, manifest and agent verification, GPO creation/link/security group filter, remote machine reboot, gpresult, agent/task installation, HKLM LockScreenImage image and content hash, scheduled second-image change, agent-managed cleanup, original lock-screen registry restoration, GPO unlink/delete and exact SYSVOL source removal.

**Limitations:** The test uses RPC and SMB from the management workstation, so network/firewall permissions must permit them. Actual screen rendering (rather than the HKLM path and image hash) still requires a console/VM screenshot check. DFS Replication is asynchronous. Full is not safe to run against production computers; a failed removal may leave a disabled or unlinked test policy or source files for human inspection.

## Privacy and report handling

- `reports/` remains **on the operator's computer**. The launcher does not use a GitHub API, network upload or stored passwords; AD/SMB traffic is confined to the domain's infrastructure.
- Files `live-ad-lab.json` contain domain/OU names, computer names, campaign IDs, GPO GUIDs and error descriptions. Treat them as **private**. Review/redact before manually sharing any report.
- The GitHub Actions ZIP is an installation-free **test kit**, not a result proving connection to your DC. Its checks run on Windows-hosted runners without AD access.
- If Full stops after GPO creation or the report says cleanup is incomplete, do not blindly rerun Full. Inspect the reported GPO name, OU link, SYSVOL path and offline client status, then perform manual cleanup or recovery.

## Troubleshooting

If Preflight fails, read `live-ad-lab.json` first; check `w32tm /query /source` on test clients, `gpresult /scope computer /r`, domain group direct members, writable DC `RootDSE.currentTime`, C$ reachability and remote Task Scheduler firewall rules. The tests require explicit Windows 11 Enterprise/Education and reject existing LockScreenImage/NoChangingLockScreen values on test PCs.

Do not run **Actions → Live AD + Windows 11 Lab (manual)** in GitHub without a self-hosted runner; that older workflow remains for installations that have one, and it is not needed by this offline kit.
