# LockScreen Campaign Manager — independent client cleanup evidence

This is an **opt-in local test only**, for one approved disposable domain-joined
Windows 10/11 Enterprise/Education computer. It covers both **SYSVOL** and
**dedicated SMB campaign payload** deployments, and PS1 or lab-only EXE agents.
It does not create/remove GPOs, change registry policy, perform network
operations against AD/SMB, execute the client agent, or uninstall software.

**Purpose:** A `Completed` client heartbeat is not proof of final cleanup.
It can be written while files/tasks still exist. A native EXE agent can only
finish deleting itself after reboot. The independent local audit reads the
actual registry, scheduled tasks, files, owner and Computer RSoP, and requires
a clean baseline captured **before** the campaign was deployed.

## Before deploying: capture Baseline

1. Export a new campaign locally with Preview.86. Obtain its unique `campaignId`
   from the generated local `campaign.json`. Do **not** create/link the GPO yet.
2. On the intended disposable test Windows PC, extract the **successful main**
   branch offline lab kit. The tools are under `tools`.
3. Run from an elevated Windows PowerShell 5.1 session on the **local test PC**,
   not from a DC or a remote administrative session:

```powershell
powershell.exe -NoProfile -File .\tools\run_endpoint_closure_local.ps1 -Mode Baseline -ExpectedComputer "TESTPC01" -CampaignId "new-campaign-id"
```

4. Read the safety explanation and enter `I_APPROVE_ENDPOINT_BASELINE`.
   The program creates `%ProgramData%\LSCM-Endpoint-Evidence\<campaign-id>\baseline.json`.
   The protected file contains a SHA-256 fingerprint of the two original
   Lock Screen registry values, whether their parent key existed, the local
   test PC/domain and capture UTC. It does **not** store original registry
   paths/values in its public report. It refuses to overwrite an existing
   baseline, reuse a completed campaign ID, or capture while a different
   campaign owns the lock screen or its tasks remain.

The baseline must be captured **before GPO creation**. If a GPO already
modified the registry, no after-the-fact reconstruction can prove restoration.

## Deploy and exercise the test campaign

Use the Manager's normal explicit **read-only Preview → approve Deploy**
flow. Select SYSVOL or SMB content mode and the approved single test computer
group. For SMB, run the separate [LocalSystem SMB canary](SMB-SYSTEM-CANARY.md)
to establish source hashes and access, then reboot.

Check the actual image at the console, scheduled changes and source outage
behavior **manually**. Archive the Manager's deployment receipt **locally**;
Preview.86 includes `campaignId`, `domain`, `gpoId` and `deployedUtc`.
Do not put private domain/receipt details in a public GitHub issue.

## After GPO unlink, cleanup and policy refresh: VerifyCleanup

1. Explicitly unlink the exact test Computer GPO through the normal audited
   GPO lifecycle flow, run the supported agent cleanup, refresh GPO and wait
   for completion. If testing native EXE, reboot and permit delayed EXE
   removal before auditing. Do **not** remove the external SMB package
   until test endpoints are independently verified.
2. On the **same local test PC** run:

```powershell
powershell.exe -NoProfile -File .\tools\run_endpoint_closure_local.ps1 -Mode VerifyCleanup -ExpectedComputer "TESTPC01" -ReceiptPath "C:\Lab\deployment-receipt.json"
```

3. Enter `I_APPROVE_ENDPOINT_VERIFYCLEANUP`. The tool stages a private
   read-only probe, runs it as LocalSystem via a temporary **local** Scheduled
   Task, then removes that temporary task and the input/config scripts.
4. Inspect `%ProgramData%\LSCM-Endpoint-Evidence\<campaign-id>\<run-id>\report.json`.

The independent probe requires **all** conditions to report
`POSTCLEANUP_LOCAL_STATE_MATCHES_BASELINE`:

- The exact original registry value fingerprint and key-presence match the
  predeployment baseline, with the same computer, domain and campaign ID.
- The specific campaign directory, Apply task, Feedback task and active owner
  are all absent; a Task Scheduler query failure is never treated as absence.
- A final agent receipt (`.postcleanup.json`) is valid and indicates
  `AfterFileAndTaskRemoval`, released ownership and registry restoration.
- A temporary protected `gpresult /scope computer /x` report confirms that
  the **exact expected GPO GUID** no longer appears in ComputerResults.
  Filtered or incomplete/ambiguous entries do not count as verified absence.
  Raw RSoP XML is removed after parsing.

`POSTCLEANUP_REQUIRES_REVIEW` or `FAIL_CLOSED` means the endpoint has
not passed independent acceptance. Do not mark it clean merely because a
client heartbeat reported `Completed`.

The sanitized JSON report never includes registry values, UNC paths, DC or
account names, full GPO XML or hashes. The admin-only baseline contains
the computer/domain name: **keep it private**. Nothing is uploaded. Do not
delete GPO source files or broaden rollout until the real endpoint tests
and any unverified status have been investigated.

### Limitations

This test does not prove the lock screen was visually displayed or that
group policy never later reapplies. It samples the current local state and
current Computer RSoP. A local administrator can alter local evidence;
it is not a remote tamper-proof attestation. GitHub-hosted CI can verify
PowerShell syntax, fail-closed behavior and packaging, but cannot replace
this live domain Windows 10/11 test.

For JPEG Photo Screen Saver `.scr`, follow the **separate** User GPO
acceptance issues #25 and #26. Automatic JPG User GPO deployment remains
disabled, even after this Lock Screen audit succeeds.
