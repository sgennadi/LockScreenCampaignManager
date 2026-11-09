# LockScreen Campaign Manager 0.4.0-preview.72 — Per-group Lock Screen and Screen Saver

## What changed

- New **Group assignments...** step inside Active Directory/GPO Deployment (Wizard and Advanced). Add up to 20 assignments with one of three modes: **LockScreen**, **ScreenSaver** or **Both**.
- Choose independent computer OU + computer security group for Lock Screen; independent user OU + user security group for Screen Saver. **Both** creates one GPO of each kind for the chosen row.
- Settings persist in the local campaign project. Existing projects with no target assignments keep their old single-Lock-Screen deployment workflow.
- A ScreenSaver-only project may be deployed without Lock Screen JPEGs. Lock Screen and Both still require validated campaign images.
- Control built-in Windows `.scr`, user idle time in seconds (60–86400) and password on resume.
- Batch Preview checks each exact OU/group and GPO name with signed/sealed LDAP; Lock Screen source image hashes and per-group unique SYSVOL campaign IDs remain checked.
- Native Screen Saver GPO creates a **User Configuration** Registry.pol, with user (upper 16 bits) GPO version, standard registry CSE GUID and user GPO security filtering. No loopback policy or domain-wide link is created.
- Batch deployment requires explicit confirmation and records each created GPO separately. If later operations fail, earlier GPOs are preserved for administrator inspection; there is no misleading atomic-success claim.
- Native EXE group-batch deployment remains disabled; PS1 is the production Lock Screen client. This feature is a preview until isolated real-DC/user/computer acceptance tests succeed.

## Critical scope distinction

Windows Lock Screen policies are computer-based (HKLM). Windows Screen Saver policies are user-based (HKCU). **A computer group is not automatically a user group.** If a school wants different screen savers based on the computer rather than on which user is signed in, that needs an *explicit* and separately verified loopback design; preview.72 does **not** silently enable loopback, since it can change unrelated effective user policies.

The built-in `scrnsave.scr` shows a **blank** screen saver. The image campaign JPEGs continue to drive Lock Screen only. A slideshow screen saver from campaign images requires a separately audited and signed `.scr` component; this release does not claim to provide that capability.

## Checks and limitations

- Windows x86/x64 full self-tests; static group-mode and scope contracts; WinForms smoke at 100–200%; User/Registry.pol signature and supported built-in values; old-project backward compatibility.
- No live DC, GPMC, RSoP or Windows user logon tests are possible on GitHub-hosted runners. First use an isolated test **user OU + security group** and review `gpresult /h` under an affected account. Confirm inherited GPO and WMI precedence.
- Operator controls are English; no administrative PowerShell script is used by the manager. The existing PS1 endpoint client remains independent.
- Public Bug Reports continue to be reviewed by the read-only diagnostics workflow. No automatic issue or client log submission.

Full operation and security notes: [Screen Saver group targeting](SCREEN-SAVER-GROUP-TARGETING.md).
