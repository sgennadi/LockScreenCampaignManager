# LockScreen Campaign Manager 0.4.0-preview.76 — Scoped Computer/User GPO conflicts

This preview extends the **read-only Group Assignments → Preview / check conflicts** path for Lock Screen, Screen Saver and Both campaigns.

## New behavior

For each independently assigned Computer Lock Screen or User Screen Saver target, the manager now:

1. Revalidates the exact selected writable DC, destination OU, security group and new GPO name.
2. Performs a separate native read-only GPO scan against that target's OU and ancestor links on the same selected DC.
3. Scans `Machine/Registry.pol` and Machine GPP Registry XML for computer policies, and `User/Registry.pol` and User GPP Registry XML for user policies. The latter recognises Screen Saver values under `HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop` and the legacy HKCU Desktop path.
4. Displays per-target **high-risk**, **warning** and relevant inherited policy candidates, together with limitations and coverage warnings. For Both, these are two different scans and two different GPOs.
5. **Fails Preview closed** if any required LDAP, SYSVOL or policy source cannot be read or verified; there is no fallback to `0 conflicts`.
6. Repeats Preview before deploying. If any static candidate overlap is detected, asks for an **additional default-No approval** before the existing new-GPO creation confirmation.

The scan never edits existing GPOs, ACLs, links, registry values, AD group membership, or SYSVOL policy files. It does not execute remote scripts, `gpupdate`, or create scheduled tasks. The installed PS1/EXE agent code is unchanged.

## What counts as a candidate overlap

Computer Lock Screen: managed `LockScreenImage` and related Windows Personalization and Spotlight policy values. User Screen Saver: `ScreenSaveActive`, `SCRNSAVE.EXE`, `ScreenSaveTimeOut`, `ScreenSaverIsSecure`; the screen-saver-settings UI restriction `NoDispScrSavPage` is a warning.

A disabled User policy half cannot override a User setting; a disabled Computer half cannot override a Computer setting. Likewise, disabled OU links and Block Inheritance are considered, but actual applicability is still estimated.

**This is not effective GPO winning-policy analysis or RSoP.** It cannot fully determine AD security token/ACL results, WMI filter evaluation, GPP item-level targeting, the client's site, local or MDM policies, script-side effects, and client-side precedence. Zero static overlaps therefore does *not* mean the deployment will win or that Screen Saver works. Test a pilot **user** with `gpresult /h` (User Configuration) and a pilot **computer** with Computer Configuration results before rolling out.

## Verification

Windows x64 and x86 hosted CI builds run offline native tests for User/Computer scope selection, MS-GPREG Screen Saver binary fixtures, correct HKCU-vs-HKLM XML detection, disabled policy-bit handling and truncated policy rejection, plus the existing WinForms DPI smoke. Hosted CI does **not** have your domain controller, GPOs, or workstation and cannot prove deployment in production.

See [Group-targeting and Screen Saver policy](SCREEN-SAVER-GROUP-TARGETING.md).

**Version:** 0.4.0-preview.76. The PowerShell/EXE client payload remains at version 0.4.0-preview.75 as no endpoint code changed in this release.
