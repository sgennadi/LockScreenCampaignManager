# LockScreen Campaign Manager 0.4.0-preview.69

## Application version is visible

The current application version appears in the **main editor header**, **Campaign Wizard header**, **AD Connection Wizard**, **AD / GPO Deployment** and **Agent Management / Live**. Window titles also include the build version. The version is derived from the packaged application assembly/CSProj, never manually maintained in several labels. These layouts are checked by Windows x86/x64 CI at multiple simulated UI scales to catch clipped titles and version labels.

## Client type and version in status reports

Both client agents continue writing **one JSON per computer per campaign** to the configured SMB share, e.g. `\\\\fileserver\\LockScreenReports$\\campaign-id\\COMPUTERNAME.json`.

- PS1 reports now include `clientType: "PowerShell"`; native EXE reports include `clientType: "Executable"`.
- Each client includes its **own agent version**. Agent Management and Client Health show both fields, with CSV export.
- Agent Management compares the *client-reported* type with the method independently discovered from the managed GPO. Disagreement appears as **Reported mismatch** and is explicitly advisory, not proof of tampering or authority to repair.
- Old schema-v1 status reports lacking `clientType` still load as **Unknown / legacy**. Invalid or unexpected type fields fail validation. A stale JSON is labeled **Stale report**, never automatically **Offline**.

## Safety and acceptance gate

No report or client-supplied value grants remote RPC authorization; AD/GPO/SYSVOL scope must still be checked. Native EXE production deployment is **disabled**: only the explicit `--native-exe-lab` isolated OU / max 5 PC canary exists. CI validates compilation, native CLI, synthetic JSON and desktop layout, but does not prove behavior on a live writable DC or domain clients. Windows 10/11 lock-screen and cleanup/restore must be verified on dedicated lab machines before production release.
