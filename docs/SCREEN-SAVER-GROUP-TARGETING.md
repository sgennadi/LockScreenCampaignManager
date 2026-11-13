## Preview.114 — verify the actual User HKCU Screen Saver in a manual pilot

On an approved Windows 10/11 PC, sign in as the test user and open **Tools → Endpoint user Screen Saver probe...** (or launch the portable manager with `--endpoint-user-probe`). Click **Capture this Windows user**, review the read-only HKCU values and explicitly save a PRIVATE local JSON. In the manager, after scanning the selected DC's GPOs and importing separate User/Computer `gpresult` XML, open **Pilot readiness... → Import user HKCU JSON...**. The computer DNS suffix/FQDN must match the pilot; user XML identity is independently checked and any different UPN/NetBIOS form remains **Not verified**. Optional remote power readings require a separate approved action. Neither registry values nor unsigned JSON prove winning GPO or visual behavior. [Full procedure](RELEASE-PREVIEW114.md).

## Preview.113 - choose local custom SCR/EXE, or create from JPG

In Wizard -> Campaign Type -> Screen Saver/Both, select **Custom .scr / .exe (local pilot only)** and click **Browse .scr / .exe...**. Only regular local PE files are accepted; UNC and symbolic links are rejected. SHA-256 and Authenticode status are shown. This never executes, installs or distributes the file. A generic EXE requires genuine Windows Screen Saver /s, /p and /c support. Changing extension alone is insufficient.

To create a Photo Screen Saver from images, enable JPG photo slideshow, select images and click Export JPG .scr pilot on Save. Verify on a separate Windows machine before rollout. GPO deployment for custom and JPG executable pilots remains intentionally blocked until a signed, verified and reversible client distribution mechanism exists.

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

## JPG photos as a Screen Saver (preview.80, local pilot)

Select **Screen Saver** or **Both**, then check **JPG photo slideshow** in Campaign Type. The Wizard then keeps Images, Arrange and Schedule enabled; choose multiple JPG/PNG image files or an entire folder, arrange them, and set **Change JPG every (seconds)**. After validation choose **Export JPG .scr pilot...** on the Save page.

The export writes `PhotoScreenSaver.scr` (a renamed restricted portable Manager apphost), `slideshow.json` and a normalized, SHA-256 checked `images` folder into a local directory. Run `PhotoScreenSaver.scr /s` on an authorized test Windows 10/11 PC. The app only reads image files; it does not change HKCU/HKLM or install the Screen Saver. Built-in Windows `.scr` remains supported as before.

**Not yet deployed by GPO:** User Configuration > `SCRNSAVE.EXE` must eventually point to an accessible protected local executable, with the matched JPGs and manifest. Running a remote SCR from SYSVOL or writing untested `EncryptedPIDL` values to HKCU would be unsafe. Hence JPG User GPO Apply is blocked until Windows endpoint and trust/ACL tests have passed. This does not affect existing built-in User GPO screen savers. The auto-update and Lock Screen agents are unchanged.

## Conflict-scan Registry.pol validation (preview.78)

The native read-only scanner now requires the MS-GPREG UTF-16LE **NUL-terminated key and value identifiers**, with a separate UTF-16 semicolon following each terminator. It rejects incomplete/malformed string values, incorrectly sized numeric values and truncated DWORD fields rather than treating them as clean policy evidence. Valid `User/Registry.pol` generated for Screen Saver and `Machine/Registry.pol` for Lock Screen remain supported.

If an existing GPO contains malformed `Registry.pol` evidence, **Preview blocks Deploy** and reports the file error; it does not rewrite existing GPO files. Use GPMC and an isolated domain pilot to investigate before proceeding. The import is not a substitute for client `gpresult` and does not prove effective policy winning precedence.

## Batch safety

Preview reads and validates **every** proposed computer or user policy separately, checks its exact OU, group, DC, GPO name, read-only source manifests and image hashes where needed, and does not change AD or SYSVOL. Starting in preview.76, each target also receives an independent read-only **OU/ancestor-linked GPO conflict audit** using the chosen DC:

- Lock Screen target: scan `Machine/Registry.pol` and `Machine/Preferences/Registry/Registry.xml`, plus detected startup-script coverage notes. Flag overlapping managed lock-screen and Spotlight settings.
- Screen Saver target: scan `User/Registry.pol` and `User/Preferences/Registry/Registry.xml` (HKCU only), plus logon/logoff-script coverage notes. Flag `ScreenSaveActive`, `SCRNSAVE.EXE`, `ScreenSaveTimeOut`, `ScreenSaverIsSecure` as high-risk candidates; show a warning for `NoDispScrSavPage`.
- Both: perform two separately scoped audits. Explicitly disabled User/Computer GPO halves and link/enforcement/inheritance state are handled independently. If any audit cannot read LDAP/SYSVOL reliably, **Preview fails** and Deploy does not proceed. High-risk/warning findings require an extra default-No confirmation. All details are in the read-only Preview.
- From preview.77, a missing User or Machine policy subfolder is **not** automatically considered corruption: an unconfigured GPO half may legitimately have no directory. The scanner checks the selected DC's AD `versionNumber` (User upper 16 bits / Computer lower 16 bits) and corresponding `gPCUserExtensionNames` or `gPCMachineExtensionNames`. A missing policy subfolder with both version and CSE list empty is skipped with a coverage note. A missing subfolder despite configured AD settings is a **blocking consistency error**. A missing `Registry.pol` when the Registry Policy CSE is advertised also blocks Preview. Unreadable paths, reparse-point directories and network errors are never treated as clean.
- The Screen Saver UI restriction `NoDispScrSavPage` is correctly checked at `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System`, not at the Screen Saver Desktop policy path. This restriction prevents opening/changing the UI; it does not by itself disable the screensaver.

The static audit cannot determine the winning client policy: **zero static overlaps does not mean conflict-free**. Security filtering, WMI, GPP item-level targeting, Site/local GPOs, MDM policy, script side effects and client RSoP remain unverified. Deploy repeats Preview, requires an explicit batch confirmation (default **No**), creates individually named GPOs, and writes individual receipts.

**This is not a transaction:** successful GPOs are **not** automatically deleted if a later assignment fails. If a batch is interrupted, stop and manually inspect the named GPOs and SYSVOL state before retrying. Use dedicated groups with no overlapping memberships. Native EXE batch deployment is blocked; use the production PS1 client for new LockScreen group batches. Existing projects with no assignment matrix still use the legacy single-target Lock Screen workflow.

The manager cannot tell from AD alone whether the user GPO wins. On a pilot **user** run `gpresult /h` and inspect **User Configuration** and the user's effective screen-saver values; on a pilot **computer** inspect the LockScreen PS1 agent and GPO. This cannot be tested on GitHub-hosted Windows runners with no connected domain.

### Source references

- Microsoft: [Group Policy processing and security filtering](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/group-policy/group-policy-processing)
- Microsoft: [Screen saver User Configuration policy settings](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-admx-controlpaneldisplay)
- Microsoft: [Registry policy file format (MS-GPREG)](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpreg/5c092c22-bf6b-4e7f-b180-b20743d368f5)
- Microsoft: [GPO version update (user upper 16 bits)](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpol/59bb540a-64f4-4c52-9c55-5ca2fd2c0270)
