# Target modes: Lock Screen, Screen Saver and Both

The campaign can have up to 20 independent **group assignments**. Open **AD / GPO Deployment → Group assignments...** after reading the selected AD controller's OUs and security groups. Add one row per assignment, name it, choose a mode, select target OUs/groups from verified lists, and save the local project.

| Mode | Required AD target | Policies created | Content |
|---|---|---|---|
| LockScreen | Computer OU + computer security group | One Computer Configuration GPO (existing PS1 Startup agent) | Campaign JPEG sequence |
| ScreenSaver | User OU + user security group | One User Configuration GPO (registry policy) | Windows built-in `.scr` |
| Both | Computer OU/group AND user OU/group | Two independent GPOs, one of each kind | Lock Screen JPEGs + built-in user screen saver |

A single group may contain both user and computer members, but their **security principals and OU applicability remain distinct**. The UI does not assume a computer group can receive HKCU user settings. For example:

- Library workstations: computer OU `OU=Library-PCs,...`, computer group `CN=Library-Computers,...` → LockScreen
- Library patrons: user OU `OU=Library-Users,...`, user group `CN=Library-Patrons,...` → ScreenSaver
- Dedicated seminar: computer OU/group for classroom PCs **plus** user OU/group for instructors → Both

## Screen Saver policy

For each ScreenSaver assignment the native C# deployer validates the writable DC and the *specific* user OU and security group via LDAP, checks for a duplicate GPO name, and independently creates a **User** policy. It generates `User/Registry.pol` per Microsoft's MS-GPREG specification, with the following user settings:

- `HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop\ScreenSaveActive` = `1`
- `HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop\SCRNSAVE.EXE` = `scrnsave.scr` (or the selected allowed built-in Windows saver)
- `HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop\ScreenSaveTimeOut` = idle seconds (60–86400)
- `HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop\ScreenSaverIsSecure` = `1` or `0`

These values are REG_SZ. The GPO user extension uses the Microsoft-defined Registry CSE and *User* administrative template extension IDs, updates the **upper** GPO version word to 65536, sets GPO flags=2 (computer policy disabled), and checks AD/SYSVOL readback before linking. A new GPO is linked only to the selected **User OU** and filtered to the chosen user security group with Authenticated Users read-only. It never enables loopback Merge or Replace. Group Policy applies the settings when that user policy is processed; the selected user must actually belong to the chosen user OU/group and no conflicting higher-precedence policy may override it.

**Important:** The built-in `scrnsave.scr` is the **blank** Windows screen saver; choosing ScreenSaver or Both does **not** yet turn campaign JPEGs into a photo slideshow. Windows screen savers are executable `.scr` programs, not lock-screen JPGs. A managed slideshow `.scr` must be built, signed and validated separately, not silently inferred from an arbitrary path or JPEG.

## Batch safety

Preview reads and validates **every** proposed computer or user policy separately, checks its exact OU, group, DC, GPO name, read-only source manifests and image hashes where needed, and does not change AD or SYSVOL. Starting in preview.76, each target also receives an independent read-only **OU/ancestor-linked GPO conflict audit** using the chosen DC:

- Lock Screen target: scan `Machine/Registry.pol` and `Machine/Preferences/Registry/Registry.xml`, plus detected startup-script coverage notes. Flag overlapping managed lock-screen and Spotlight settings.
- Screen Saver target: scan `User/Registry.pol` and `User/Preferences/Registry/Registry.xml` (HKCU only), plus logon/logoff-script coverage notes. Flag `ScreenSaveActive`, `SCRNSAVE.EXE`, `ScreenSaveTimeOut`, `ScreenSaverIsSecure` as high-risk candidates; show a warning for `NoDispScrSavPage`.
- Both: perform two separately scoped audits. Explicitly disabled User/Computer GPO halves and link/enforcement/inheritance state are handled independently. If any audit cannot read LDAP/SYSVOL reliably, **Preview fails** and Deploy does not proceed. High-risk/warning findings require an extra default-No confirmation. All details are in the read-only Preview.

The static audit cannot determine the winning client policy: **zero static overlaps does not mean conflict-free**. Security filtering, WMI, GPP item-level targeting, Site/local GPOs, MDM policy, script side effects and client RSoP remain unverified. Deploy repeats Preview, requires an explicit batch confirmation (default **No**), creates individually named GPOs, and writes individual receipts.

**This is not a transaction:** successful GPOs are **not** automatically deleted if a later assignment fails. If a batch is interrupted, stop and manually inspect the named GPOs and SYSVOL state before retrying. Use dedicated groups with no overlapping memberships. Native EXE batch deployment is blocked; use the production PS1 client for new LockScreen group batches. Existing projects with no assignment matrix still use the legacy single-target Lock Screen workflow.

The manager cannot tell from AD alone whether the user GPO wins. On a pilot **user** run `gpresult /h` and inspect **User Configuration** and the user's effective screen-saver values; on a pilot **computer** inspect the LockScreen PS1 agent and GPO. This cannot be tested on GitHub-hosted Windows runners with no connected domain.

### Source references

- Microsoft: [Group Policy processing and security filtering](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/group-policy/group-policy-processing)
- Microsoft: [Screen saver User Configuration policy settings](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-controlpaneldisplay)
- Microsoft: [Registry policy file format (MS-GPREG)](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpreg/5c092c22-bf6b-4e7f-b180-b20743d368f5)
- Microsoft: [GPO version update (user upper 16 bits)](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpol/59bb540a-64f4-4c52-9c55-5ca2fd2c0270)
