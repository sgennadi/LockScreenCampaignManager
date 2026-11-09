# LockScreen Campaign Manager 0.4.0-preview.68 — Dual PS1 / Native EXE Lab Integration

## Campaign Wizard: select the client and the GPO startup method

On **AD / GPO Deployment → Step 2 (target OU and computer security group)** select one of two client types:

- **PowerShell (.ps1) — PowerShell Startup Scripts** (production default): the currently supported `LockScreen-Agent.ps1 -Mode Install`, registered through the GPO `Machine/Scripts/psscripts.ini` startup method.
- **Native (.exe) — classic Startup Scripts** (EXPERIMENTAL / LAB-ONLY): a pinned `LockScreen-Agent.exe --client-agent Install` registered directly in `Machine/Scripts/scripts.ini`. No PowerShell or CMD wrapper.

Changing the client invalidates the previous GPO Preview. Preview verifies the manifest client type and executable/package hash. GPO Deploy independently revalidates the method and immutable artifacts; existing managed GPOs cannot be silently converted from PS1 to EXE or vice versa.

## EXE production deploy remains blocked

`NativeClientAgent.IsReadyForProduction = false`. The standalone native EXE client has not passed live GPO/client acceptance on a real writable DC and Windows 10/11 domain endpoints. Its **read-only Preview** is available; ordinary Deploy remains disabled. The packaged Manager is the .NET WinForms application; the headless EXE agent is a pinned copy using `--client-agent` modes.

Only for an operator-controlled, **isolated TEST OU and a flat test security group with 1–5 directly listed computer accounts**, the manager may be started as:

```cmd
LockScreenCampaignManager.exe --native-exe-lab
```

The separate risk warning and explicit confirmation remain required; the native deployment code revalidates the exact group and OU membership before creation and linking. This switch is not a production-readiness assertion.

## Agent Management

Agent Management / Live retains scoped **Push / Repair / Report / Cleanup**. The native remote task dispatcher selects PS1 vs EXE from the verified campaign package (EXE tasks invoke the pinned exe directly). Every remote action still requires Preview, exact managed GPO GUID confirmation, and independent AD/GPO/SYSVOL scope rechecking. Untrusted SMB reports never authorize a remote operation.

Client Health continues to use one report file per computer, `\\\\server\\share\\<campaign-id>\\COMPUTERNAME.json`. The native EXE reports the same schema-v1 fields as the PS1 agent: campaign ID, computer name, UTC time, version, stage, health, image/registry/task/file status. The status JSON is diagnostic evidence, not an authenticated heartbeat.

The **preview.67** changes remain: shared AD user-selection/password Wizard, read-only OU and group coverage, 30/60/90/180/365-day AD activity filters, RPC/SMB status distinction, Client Health share configuration in the campaign Schedule Wizard, and accessible Preview.

## CI coverage and next acceptance gate

The Windows x86/x64 pipeline verifies both agent methods' packaging contracts, strict native mode dispatch, native read-only Status, SHA-256 tamper refusal, managed classic GPO startup signatures, scheduled image ordering, EXE preview package, and WinForms layout and image pixels. GitHub Actions cannot test effective delegated AD permissions, startup execution on a real client, domain Group Policy replication, on-screen lock-screen rotation, or actual EXE cleanup and restore after reboot. **Do not deploy EXE in production until these end-to-end checks pass.**
