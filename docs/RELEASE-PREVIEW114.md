# LockScreen Campaign Manager — 0.4.0-preview.114

## What's new

1. **Local User HKCU probe:** On an authorized Windows 10/11 pilot PC, open **Tools → Endpoint user Screen Saver probe...**, or launch the portable EXE with `--endpoint-user-probe`. Click **Capture this Windows user** to view the actual process user's Screen Saver HKCU values. It neither deploys nor runs a selected Screen Saver executable.
2. **Private, opt-in JSON:** After preview and a default-No confidentiality confirmation, **Save local HKCU JSON...** exports a strictly limited, unsigned file to a local folder only. No telemetry, server submission, UNC output, automatic background collection or overwrites.
3. **Correlate with scanned DC GPOs:** In **GPO Operations → Conflicts → Scan all DC GPOs → Compare gpresult XML... → Pilot readiness...**, use **Import user HKCU JSON...**. The imported file must match the chosen pilot PC FQDN and domain. Clear it with **Clear HKCU evidence**. Changing the pilot computer or OU invalidates the imported snapshot.
4. **Actionable HKCU checks:** Compare user policy and desktop settings for `ScreenSaveActive`, `ScreenSaveTimeOut`, `SCRNSAVE.EXE`, `ScreenSaverIsSecure`, and the independent `NoDispScrSavPage` UI restriction. Show disabled/zero or later-than-campaign idle values, disagreement between registry sources, absent/unreadable values and stale evidence separately.
5. **Three-language interface and safety CI:** New English/Russian/Hebrew probe controls, WinForms high-DPI smoke, strict schema and privacy tests, Windows x64/x86 builds and offline AD regression checks.

## Pilot procedure

1. Choose a **dedicated test Computer OU and User OU/security groups**; do not publish or link any new GPO for this check. In the manager, run **Scan all DC GPOs** against an explicitly selected DC and review source-read issues.
2. On the **actual Windows 10/11 pilot PC**, sign in as the *test user*, run `LockScreenCampaignManager.exe --endpoint-user-probe` (or open **Tools → Endpoint user Screen Saver probe...**), select **Capture this Windows user**, review the user/domain/computer and registry values, then explicitly **Save local HKCU JSON...** to a protected LOCAL folder. Do **not** launch elevated as a different administrator, because the HKCU would belong to that administrator.
3. Independently collect **Computer** and **User** `gpresult /x` XML from the intended contexts. Import both via **Compare gpresult XML...**. These are manually supplied files, not signed endpoint attestations; `DOMAIN\user` and UPN strings may differ. Do not assume imported user XML necessarily belongs to the same interactive session.
4. Open **Pilot readiness...** and type the exact domain computer FQDN. Choose its already-scanned Computer OU. Click **Import user HKCU JSON...** and explicitly select the protected local file. Use **Verify power (PC)...** only when you authorize the existing separate read-only remote WMI/HKLM power observation.
5. Review *Observed concern*, *Source candidate* and *Not verified* separately, including whether screen-off/sleep/lock timeouts occur **before** the campaign's configured Screen Saver idle seconds. Verify real Screen Saver activation, wake, resume password and monitor behavior manually on Windows 10 and Windows 11.

## Security and limitations

- The HKCU capture reads **only current-process-user** `Registry.CurrentUser` with read-only keys. No GPO/AD/registry writes, deployment, remote commands, gpupdate, scheduled jobs, Screen Saver execution or web requests. The remote computer power observation is a **separate manual action** with its existing LDAP/DC/OU/computer approval gate.
- JSON is **unsigned and user-editable**. Domain and computer are local OS DNS labels, not cryptographically proved AD membership. SHA-256 detects byte differences **only against a separately trusted reference**; the digest inside the manager is not an authenticity guarantee.
- Import rejects unknown/duplicate JSON fields, incorrect setting scopes, missing rows, oversized inputs, future dates, mismatched computer/domain, UNC paths and reparse-point folders/files. A snapshot older than **10 minutes** is **Not verified** and cannot generate a fresh observed-concern verdict. The manager does not retain or upload the imported JSON.
- A policy key's presence, manually imported `gpresult` flags, Windows HKCU observations and WMI power settings **do not prove winning per-setting GPO, visual display or root cause**. A hidden Screen Saver settings page (`NoDispScrSavPage`) does not necessarily disable the Screen Saver itself.
- No automated remediation. Automatic deployment of custom SCR/EXE or JPG Photo Screen Saver through User GPO remains blocked pending real Windows/AD, signature, safe distribution, access-control and rollback acceptance.

**Release boundary:** GitHub-hosted CI verifies code contracts and offline WinForms builds, **not** a live AD domain or a physical Windows 10/11 display. The Windows/AD and Hebrew 200% DPI acceptance gates remain open.
