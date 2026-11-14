# LockScreen Campaign Manager — 0.4.0-preview.120

## What's new

1. **Local SYSTEM SYSVOL read pilot.** Add a manually approved Windows 10/11
   canary that reads a selected GPO's `GPT.INI`, `User\Registry.pol` and
   `Machine\Registry.pol` through the selected DC's SYSVOL UNC as the
   computer's LocalSystem identity. No remote execution or GPO modifications.
2. **One unified Pilot Readiness.** Import the local private JSON as optional
   evidence into the existing GPO analyzer. A new SYSVOL / LocalSystem finding
   shows the exact GPO GUID and file states alongside GPC ACL, OU, loopback,
   Screen Saver and power observations, without requiring gpresult XML.
3. **Strict identity and freshness checks.** Reject wrong domain, DC,
   computer, malformed GUID, non-SYSTEM context, invalid file statuses,
   duplicate JSON fields, future time, unsafe local import paths and reports
   larger than 16 KiB. Require correlation to a fresh non-excluded scoped
   GPO link before assigning source-review priority.
4. **Russian/Hebrew and offline regression gates.** Add localized import and
   clear controls, high-DPI WinForms smoke coverage, PowerShell 5.1 syntax
   checks and a source/privacy regression suite for Windows x86/x64 and
   Offline Lab Kit packaging.

## Test procedure

1. Download the Windows build and offline test kit, and review
   `SYSVOL-SYSTEM-PILOT.md` from the kit.
2. Run the explicitly approved local harness on a *nonproduction*
   domain-joined Windows 10/11 PC using its exact computer FQDN, DC and GPO GUID.
3. Import its private JSON into **GPO Operations → Conflicts → Pilot Readiness**
   using **Import SYSTEM SYSVOL JSON...**.
4. Compare any reported denied/missing/unavailable `GPT.INI` with the
   selected DC's source scan, GPO filtering, actual machine SMB/NTFS access
   and the signed-in pilot user's RSoP. Missing User Registry.pol alone may
   be normal when the policy uses another CSE.

## Safety / limitations

The probe verifies only read access by one local SYSTEM process through
the specified UNC. DFS routing, live computer token, group policy CSEs,
GPP/WMI/site targeting, GPO precedence, actual User policy application,
Screen Saver launch and screen visuals remain unverified. Its JSON is
unsigned operator-supplied evidence, not an authenticated endpoint receipt.
The temporary local task and files are subject to explicit approval; no
AD/GPO/SYSVOL writes, gpupdate or remote tasks. Real Windows 10/11 domain
pilot acceptance is required before production.
