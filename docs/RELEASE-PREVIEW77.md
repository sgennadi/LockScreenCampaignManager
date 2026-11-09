# LockScreen Campaign Manager 0.4.0-preview.77 — GPO coverage and Screen Saver consistency

This preview corrects two problems in the native, read-only GPO pre-deployment audit.

## 1. No false corruption error for an unconfigured GPO policy half

A GPO may configure only **Computer Configuration** or only **User Configuration**. In that case, its other SYSVOL policy subdirectory can be absent. Preview.76 treated the absent `User` or `Machine` directory as a hard failure even when the GPO had no settings in that half.

The scanner now reads **AD metadata on the exact selected DC** for every linked GPO: `versionNumber`, `gPCUserExtensionNames`, `gPCMachineExtensionNames` and policy flags. It interprets the upper 16 bits of the version as User and lower 16 bits as Computer.

- If the chosen policy half has zero version, no registered extensions and no SYSVOL directory, it is reported as an **unconfigured GPO half** and safely skipped.
- If the policy half has a nonzero version or lists an extension but lacks its folder, **Preview fails closed** and calls for a DC/SYSVOL consistency check.
- If an *applicable* GPO advertises the Registry Policy CSE GUID but lacks its `Registry.pol`, **Preview fails closed** instead of claiming no conflict. A disabled/non-applicable policy half cannot override this target.
- Access denied, network errors, unexpected files instead of directories, and reparse-point/junction directories still fail. GPP evidence path ancestors are verified against reparse points.

The scan is read-only: it does not repair the GPO, change AD/SYSVOL, or edit Group Policy links.

## 2. Screen Saver registry location fixed

`NoDispScrSavPage` is now correctly detected under

`HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System`.

This is the policy that **prevents changing Screen Saver via Control Panel**. It does *not* disable Screen Saver execution by itself. The four actual Screen Saver operating settings remain under the HKCU Desktop policy paths and continue to be reported independently.

## Testing and deployment

- Hosted Windows x64/x86 CI builds and manager self-tests exercise the policy-half version bits, CSE metadata checks, GPP HKCU key detection, truncated policy handling, and read-only scope invariants.
- GitHub-hosted Windows is not domain-joined to your OU. It cannot verify actual GPO scope, security filtering, WMI, MDM, script outcomes, SYSVOL replication or user/computer `gpresult` on endpoints.
- Perform a pilot test with a domain **user** and **computer** to establish real RSoP before relying on production policy precedence.

**Manager version:** 0.4.0-preview.77. The PowerShell and native EXE client agents are unchanged from preview.75.

Microsoft policy references:
- [Registry Policy CSE identifiers (MS-GPREG)](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpreg/f0dba6b8-704f-45d5-999f-1a0a694a6df9)
- [Screen Saver UI restriction (ADMX_ControlPanelDisplay)](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-controlpaneldisplay)
