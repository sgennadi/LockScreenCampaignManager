# SMB campaign source — real LocalSystem endpoint canary

This is an **isolated test**, not proof that a campaign is safe to deploy broadly.
The Preview.85 tools verify read-only access to an immutable SMB campaign
under **the test computer's LocalSystem context**. They do **not** change AD,
GPOs, SYSVOL, the lock-screen registry or the campaign's source files.
The only temporary change is a **local** Scheduled Task, removed after testing.
A restricted local JSON report is retained for review. Nothing is uploaded.

## Prepare

1. In the Manager Preview.85 Deployment Wizard, select **SMB network folder (UNC)**.
   Prepare a dedicated protected network share beforehand. On *both* SMB share
   and NTFS ACLs, grant the approved test computer account (or scoped Domain
   Computers) **Read & Execute**; trusted administrators only may write.
   Do not grant Authenticated Users or Domain Computers write permission.
2. Select a small disposable computer security group and isolated test OU.
   Complete read-only Preview and GPO conflict review before explicitly
   deploying the test Lock Screen campaign. Test the PowerShell client first.
   The EXE client remains subject to its separate lab-only gate.
3. In the Manager's Deployment success window, note the **local receipt file**.
   Preview.85 includes `contentSource=SmbShare`, `campaignId`,
   `packageStoragePath`, `contentShareRoot`, `domain`, `clientType`,
   `manifestSha256`, `agentSha256`, exact `gpoId` and `deployedUtc`. Older receipts without the deployment timestamp are **not sufficient**. Do not guess missing values.
4. Copy that JSON receipt to the approved test client locally (e.g.
   `C:\Lab\deployment-receipt.json`). Copy both canary tools from the
   successful **main** branch's offline-lab ZIP: `run_smb_canary_local.ps1`
   and `smb_system_canary_probe.ps1`, plus `smb_canary_rsop.ps1`, into the same local folder.
   Never put credentials into the receipt or send it to a public issue.

## Execute on the *test client*, not on the DC

Open an **elevated Windows PowerShell 5.1** window on that client.
Run:

```powershell
powershell.exe -NoProfile -File .\run_smb_canary_local.ps1 -ReceiptPath "C:\Lab\deployment-receipt.json" -ExpectedComputer "TESTPC01"
```

Review the explicit consent message, then type `I_APPROVE_SMB_CANARY`.
The harness refuses another computer/domain or a non-SMB receipt. It copies
the **local read-only probe** and a minimal config into a protected
`%ProgramData%\LSCM-SMB-Canary\<run-id>` folder, runs the probe as
LocalSystem through a temporary **local** Scheduled Task, and cleans up the
task, probe and transient config afterward. The only persistent artifact is
`report.json` in that protected local folder. It contains coarse statuses,
build number and failure codes, not UNC paths, domain names, SIDs, accounts,
hash values, raw `gpresult` output or GPO names. A protected temporary RSoP XML file is deleted when the probe finishes. Review even this sanitized
report before sharing it.

## How to interpret results

`SOURCE_VERIFIED_AS_LOCAL_SYSTEM_ONLY` means that this domain-joined test PC,
using LocalSystem, could read the selected SMB source and verified the exact
`campaign.json`, agent executable/script and every JPEG against the
original immutable manifest. If the local campaign is already installed, the
probe also checks local cache hashes; if it is not installed yet,
`LOCAL_CACHE_NOT_INSTALLED_YET` is only a note.

`FAIL_CLOSED` means that at least one identity, accessibility or integrity
check could not be completed. It is **not** safe to deploy. Inspect the
`findings` codes locally and fix the underlying problem, then export a
**new** immutable campaign if the source changed. Never overwrite a deployed
campaign ID.

The tool also reports whether the test computer rebooted after the recorded deployment time and whether the **exact expected Computer GPO GUID** appears applied in the endpoint's `gpresult /scope computer /x` RSoP, with permissive filtering flags. This does **not** prove that the Startup script ran at boot or that any particular setting won. A new `POSTBOOT_SOURCE_AND_COMPUTER_RSOP_OBSERVED_ONLY` status requires the post-deploy boot, matching RSoP, source hashes and verified local cache. The simpler source-only result remains available during early testing.\n\nThe tool does **not** prove the share was reachable *during computer boot*,
that the lock screen policy was the winning registry setting, or that Windows displayed the lock screen,
that scheduled image rotation worked, or that original registry values were
restored. Those remain separate hands-on acceptance checks:

- Reboot the disposable PC, inspect Startup/Task Scheduler logs, then run the
  canary again. Verify the local cache and agent health.
- Generate `gpresult /h C:\Lab\gpresult.html` privately and inspect actual
  Computer policy applicability, security filtering and inherited conflicts.
- Observe scheduled Lock Screen changes and confirm the displayed image.
- Unlink the approved test GPO, complete managed client cleanup, and verify
  restoration and task removal **before** manually removing the SMB source.

Do **not** use these tools on a production workstation or on the DC.
A successful hosted GitHub Actions check never substitutes for this
domain-joined Windows 10/11 acceptance test.
