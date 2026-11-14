# SYSTEM SYSVOL read pilot — local Windows 10/11 only

This is an **opt-in, isolated test** of whether one selected domain-joined
Windows computer can open one selected GPO's files through
`\\<dc>\SYSVOL\<domain>\Policies\{<GPO-GUID>}` in the **LocalSystem**
security context. It is not a Group Policy application test and cannot prove
which GPO won for any user, whether the Screen Saver starts, or whether DFS
served the specified DC.

## Before running

- Use a disposable **domain-joined Windows 10 or Windows 11** test computer.
- Choose an exact DC FQDN, full computer FQDN and **GPO GUID** seen in the
  manager's **Analyze AD scope (no XML)** report, not a policy display name.
- Work with an elevated **Windows PowerShell 5.1** session on that computer.
  Keep both scripts together inside the Offline Lab Kit's `tools` folder:
  `run_sysvol_system_probe_local.ps1` and `sysvol_system_probe.ps1`.
- Verify the original kit integrity and confirm the computer belongs to the
  intended test domain. No production rollout is authorized by this pilot.

## Local operator command (example placeholders)

```powershell
powershell.exe -NoProfile -File .\tools\run_sysvol_system_probe_local.ps1 -Domain example.com -Dc dc01.example.com -ComputerFqdn pc01.example.com -GpoId 01234567-89ab-cdef-0123-456789abcdef
```

The harness asks for `I_APPROVE_SYSVOL_READ` **before** temporarily
registering a **local-only** SYSTEM scheduled task. It stores configuration and
an unsigned private JSON report under a restricted
`%ProgramData%\LSCM-SYSVOL-Canary\<random-id>` folder. The task runs
`sysvol_system_probe.ps1` locally, reads at most one byte each from
`GPT.INI`, `User\Registry.pol` and `Machine\Registry.pol`, then
terminates. The task, staged script and configuration are removed; the
protected local report is retained. If an exception occurs, check local Task
Scheduler history and inspect any remaining private temporary files.

## Interpret the report

- `GPT.INI = Read`: this process could open the file from the requested
  UNC path. It is **not** proof that Group Policy or a User Screen Saver GPO
  applied.
- `Denied`: permission was rejected in the local SYSTEM read attempt;
  inspect selected computer's share/NTFS rights, the path, and DC/DFS routing.
- `Missing`: file was absent on this path (not necessarily permission denial).
- `Unavailable`: read/network error; cannot distinguish connection problems
  from all permissions issues.
- `Unsafe`: unsafe linked/empty/oversized file was not trusted or read.
- `User\Registry.pol = Missing` is **not inherently a blocker**: the GPO
  might have no registry policy in that half or use another extension.

Never copy unredacted private reports to public GitHub issues. They contain
internal domain/computer/DC identifiers and GPO GUIDs.

## Correlate with the one analyzer

On the manager, open **GPO Operations → Conflicts → Scan all DC GPOs →
Analyze AD scope (no XML)...** for the *same* DC, computer and OU.
Under **Pilot Readiness**, choose **Import SYSTEM SYSVOL JSON...** and
select the protected local report after secure transfer. The report is
strictly matched to the selected PC/domain/DC; its GUID must appear in the
fresh, non-excluded AD link projection to receive an interpreted status.

The manager labels these findings as operator-supplied **unsigned** evidence.
No scan imports the report automatically, and no process executes on a remote
computer. Use actual Windows User/Computer RSoP, interactive HKCU, visual
Screen Saver behavior, power settings and SYSVOL ACLs for final acceptance.

## Permissions and changes

The local harness creates/removes only a temporary local Scheduled Task and
temporary private working files with explicit confirmation. The probe itself
opens remote policy files **read-only** and writes only its protected **local**
JSON result. It never alters AD/GPO/SYSVOL, does not run gpupdate, or execute
remote source files. Running on one PC does not test an entire group or OU.
