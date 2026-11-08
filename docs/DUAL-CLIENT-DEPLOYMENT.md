# Dual client deployment — PS1 and native EXE (draft)

## Selector in AD / GPO Deployment Wizard

The administrator chooses **Client / GPO method** at the OU and computer-group step:

| Client | GPO setting | Startup command | Scheduled changes |
| --- | --- | --- | --- |
| PowerShell (.ps1) | Computer Configuration → Policies → Windows Settings → Scripts (Startup/Shutdown) → **PowerShell Scripts** | `LockScreen-Agent.ps1 -Mode Install` through `Machine/Scripts/psscripts.ini` | LocalSystem Windows PowerShell client task |
| Native (.exe) | Computer Configuration → Policies → Windows Settings → Scripts (Startup/Shutdown) → **Scripts** | `Machine/Scripts/scripts.ini` invokes the GPO-owned `Startup/Start-LockScreen-Agent.cmd`, which calls the SHA-256-pinned `LockScreen-Agent.exe --client-agent Install` | LocalSystem Task Scheduler executable action, without PowerShell |

The selected method belongs to the campaign project. Changing it invalidates the prior AD Preview; deployment independently checks that the chosen method matches the campaign manifest and source hashes. A GPO can have only one of these manager-owned startup methods. Never silently convert existing GPO startup methods.

## Backward compatibility and packaging

Legacy schema-1 campaigns omit `clientType` and remain PowerShell campaigns. This is the default for existing projects and new campaigns until the administrator chooses EXE. The EXE package contains `campaign.json`, `images/`, and `LockScreen-Agent.exe` instead of `LockScreen-Agent.ps1`. Both use `agentSha256`, strict campaign IDs, fixed image filenames and checked image digests. Exported packages are immutable after their first deployment.

The native client is an independent headless mode of the portable application executable, copied under the pinned agent filename. It does not call PowerShell. It supports `Install`, `Apply`, `Repair`, `Cleanup`, `Status`, and `Report`; changes to Windows require LocalSystem and use a shared named mutex. Local tasks are registered through Task Scheduler COM. The original registry backup is captured at activation rather than installation, and ownership checks avoid overwriting a different campaign's lock-screen policy.

## Current limitations — EXE must not be deployed to production

**`NativeClientAgent.IsReadyForProduction = false`.** The EXE option is visible in the Deployment Wizard and can participate in design and review, but EXE export/deployment intentionally fails until real-domain endpoint testing and approval. The default PowerShell option remains functional.

Automated x86/x64 compilation, source contracts, headless Status and SHA-256 tamper tests do **not** prove that a GPO actually executes on a client, that scheduled pictures appear on the physical lock screen, or that cleanup succeeds at reboot. Validate at least one isolated Windows 10 and one Windows 11 Enterprise/Education client, with no production OU/groups, before enabling EXE export.

A running executable cannot delete itself. The native engine schedules its executable for deletion at next reboot and must observe a subsequent startup to finalize the cleanup receipt. Until that happens, do not report full post-removal attestation. The EXE `Reuse` lifecycle action is disabled; create a fresh GPO for a different EXE campaign. Cross-method in-place migration is not supported.

## Acceptance checklist for the isolated test OU

- Test **PS1** first to confirm prior campaign / policy behavior did not change.
- Test **EXE** only after the safety gate is consciously removed on a dedicated test build. Confirm that `scripts.ini` and the GPO startup CMD, not `psscripts.ini`, are present.
- Apply `gpupdate`, reboot, and confirm an administrator-selected client successfully starts as SYSTEM without a GUI or PowerShell.
- Verify startup installation, the configured first slide, exact scheduled changes, time-zone / DST handling and the daily self-check.
- Verify image hash repair, broken manifest/agent refusal, owner collision, conflicting registry policy refusal and delegated GPO permissions.
- Check native Status/Report, Task Scheduler actions, deferred Push/Repair, and clean removal with post-reboot receipt.
- Verify the updated computer security group and intended OU are the only policy recipients.

**No GPO or SYSVOL modifications occur by opening the Wizard or performing read-only Preview.**
