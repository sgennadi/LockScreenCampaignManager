# Live Active Directory + Windows 11 Lab (GitHub Actions)

This is a real, manual, domain-connected integration test, not a simulation. The ordinary Windows x86/x64 build runs without access to a private AD. Do not claim live success until a connected lab job completes successfully.

Workflow: .github/workflows/live-ad-win11-lab.yml. Actions > Live AD + Windows 11 Lab (manual) > Run workflow. Choose preflight first, then full. Type the exact confirmation phrase I_APPROVE_AD_LAB_TEST. Use the main branch only.

## One-time protected setup

1. In repository Settings > Environments, create ad-lab; require reviewer approval and restrict deployment to main.
2. Install a dedicated self-hosted Windows x64 GitHub runner on a domain-joined management computer (never one of the clients under test); add custom label lscm-ad-lab. The workflow demands [self-hosted, Windows, X64, lscm-ad-lab]. A Microsoft-hosted runner must never handle live AD.
3. Run the runner service under a tightly delegated *domain service identity* with rights to create/delete a test GPO, link/unlink only the test OU, read AD using signed/sealed LDAP, write the test package into SYSVOL scripts/LockScreenCampaigns, access the clients' remote Task Scheduler over RPC and their C$ administrative SMB shares. Do not store an AD password in GitHub. Do not use Domain Admins if scoped delegation is sufficient.
4. Create a new isolated OU whose first RDN begins OU=LSCM-CI (e.g. OU=LSCM-CI,OU=Test,DC=example,DC=com) and a nonprivileged security group starting with CN=LSCM-CI (e.g. CN=LSCM-CI-Computers,OU=Groups,DC=example,DC=com). The OU must contain only the selected 1–3 disposable Windows 11 Enterprise/Education clients; the group must contain exactly those PCs as direct members. Do NOT select production computers.
5. In GitHub environment ad-lab define variables LSCM_LAB_DOMAIN (example.com), LSCM_LAB_DC (dc01.example.com), LSCM_LAB_OU_DN (full OU distinguished name), LSCM_LAB_GROUP_DN (full group distinguished name), and LSCM_LAB_CLIENTS (one to three short PC names separated by semicolons, e.g. WIN11CI01;WIN11CI02). All names refer to the lab only.
6. On each test PC enable remote Task Scheduler (RPC), administrative SMB and computer GPO startup-script processing. Ensure Windows Time sync to the domain, no existing Lock Screen policy, no LSCM campaign/tasks/active owner. Running FULL will really reboot the test PCs.
7. For extra isolation, use test VMs and a lab subnet. Do not let an untrusted pull request control this runner.

The workflow is workflow_dispatch only (no push, PR, schedule or automatic triggers), runs on main, requires typed confirmation and environment approval. Without an online authorized runner there can be no real DC/PC test.

## Test stages

Preflight verifies writable selected DC and RootDSE.currentTime, controller/DC skew under two minutes, OU membership and exact group membership, inherited lock-screen conflicts, then checks every target PC as SYSTEM for Windows 11 Enterprise/Education, domain join, a valid Windows Time source and a pristine state. Preflight temporarily creates the lab probe and a SYSTEM task on the selected lab PC; it does not create a GPO or alter lock-screen settings.

Full repeats preflight and creates a unique two-image JPEG campaign with SHA-256 manifest and the production agent; ID lscm-ci-<run_id>-<attempt>, GPO name LSCM-CI-Lab-<campaign_id>. Campaign time is UTC obtained from the selected DC. The first image starts two minutes before DC time, the second starts 15 minutes after; end is 60 minutes after. The controller calls the actual existing GPO-Deploy.ps1 in Plan and Deploy modes (not a mock). It links the GPO only to the named test OU and applies the selected test group as security filter.

The controller allows initial SYSVOL replication, remotely reboots each lab PC through Task Scheduler, requires proof of a changed Windows boot timestamp and polls actual agent health on each PC. It checks applied GPO in gpresult, local agent/manifest/image SHA-256, the SYSTEM task and the actual HKLM LockScreenImage registry value. It checks the second image after the scheduled change. A full run can take roughly 20–40 minutes, with a 90 minute maximum.

After the test the controller uses the production Gpo-Lifecycle.ps1 Preview and Apply methods to unlink its own test GPO, runs the original agent Cleanup on the clients, verifies task/removal and lockscreen restoration, deletes only the verified run-specific GPO and source SYSVOL folder. Incomplete cleanup marks the test failed. On failure it attempts to unlink and clean clients, but preserves GPO/source artifacts if complete cleanup cannot be proven. Administrative manual inspection may be needed after a partial deployment.

## Evidence and limitations

The workflow uploads live-ad-lab.json in a seven-day private Actions artifact, recording status per PC, GUID and cleanup status. No passwords are written to GitHub. A green Build portable EXE job is NOT a live AD test.

The test verifies Windows registry policy, hashes and Task Scheduler state; this is not visual proof that the lock screen actually rendered. Confirm display from an interactive VM/console independently. DFSR/AD replication remains asynchronous; clients follow domain Windows Time, not recurring polling of the DC. Real 200% DPI acceptance requires interactive validation.

If the live job is queued indefinitely, check the runner label/online status, protected environment approval and all five environment variables. Never change the live job runner to windows-latest.
