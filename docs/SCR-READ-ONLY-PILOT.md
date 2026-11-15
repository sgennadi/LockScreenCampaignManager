# Local user Screen Saver file / power pilot

This pilot is for **one approved Windows 10/11 test PC and signed-in user**. It is not an automated deployment, repair or binary execution tool.

The local **Endpoint user Screen Saver probe** reads the existing Screen Saver-related HKCU registry values and captures a single file availability observation of the candidate `SCRNSAVE.EXE` value. The field is optional in the existing strict private JSON v1 schema, so older reports remain readable.

- The source is `PolicyHKCU` if that value is present; otherwise `DesktopHKCU`. If either source is unreadable, the file check is `Unavailable` to avoid presenting the desktop value as a confirmed policy.
- Supported candidates are local `.scr` and `.exe`, including a plain filename resolved in the local Windows system directory. No switches or command-line arguments are parsed.
- The probe refuses UNC/network shares and mapped non-fixed drives, linked/reparse file or ancestor directories, and unresolved/unsupported path types. It opens the local file with read-only access and reads one byte, then closes it. **It never starts, loads or executes the file**, computes the executable hash or contacts another server.
- The private JSON stores the source, a fixed status and SHA-256 of the original HKCU registry string to detect accidental mismatches; this is **not authentication**. HKCU registry strings themselves remain present in the private JSON; do not upload it to public issues.
- Import into **Pilot Readiness → Import user HKCU JSON...** for the exact selected computer, user and currently verified user SID. No matching SID or stale AD/HKCU data means **Not verified**, not a file-related observed concern.

For power comparison, choose **Verify power (PC)...** after the current AD scope check. The analyzer compares **the actual selected user's HKCU ScreenSaveTimeOut** against current observed display, sleep and machine lock settings. AC and DC are displayed separately. Different WMI active-plan and HKLM policy values yield **Not verified**: the observation cannot establish the effective winning source.

A readable file plus compatible timers is not proof of Screen Saver animation, file signature, launch switches, session unlock security, Group Policy precedence or Windows behavior. Verify manually in-session on the pilot PC. Neither the probe nor the comparison modifies HKCU/HKLM, GPO, SYSVOL, Windows tasks, client state or network shares.
