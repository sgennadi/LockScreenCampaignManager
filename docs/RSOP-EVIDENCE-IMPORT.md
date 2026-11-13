## Preview.111 — Pilot readiness (read-only)

After using **Scan all DC GPOs → Compare gpresult XML...**, type the full FQDN of the selected Windows test computer and optionally import separate Computer/User gpresult XMLs. Click **Pilot readiness...** to open a read-only checklist that combines the previously scanned DC GPO source candidates with the imported GPO GUID-and-scope observations. It does not verify interactive User HKCU, effective setting precedence, or the actual display.

Choose the correct Computer OU from the scanned OU selector. If **Verify power (PC)...** is available, approve its separate prompt to read the **exact** computer's current active plan and policy registry from a freshly verified DC/OU/FQDN. It uses existing remote read-only WMI/HKLM, **not** a startup script, gpupdate, agent command, scheduled task, or GPO write. Display-off AC/DC, Sleep AC/DC and machine inactivity lock are compared to the *configured* User Screen Saver idle interval; equal timeouts are potential ordering concerns and zero ("Never") is not an early timeout. WMI/registry disagreements and fresh shorter timers are shown as **Observed concern**, but this is not proof that Screen Saver actually failed. Stale/partial observations remain **Not verified**. Without an OU selection or valid Screen Saver interval, pilot assessment and power verification are disabled.

The checklist always retains **Not verified** rows for signed-in User HKCU/interactive session and visual Windows 10/11 lock-screen and Screen Saver acceptance, even if source files and imported GPO IDs match. This is a manual pilot aid, **not** a pass/fail deployment certification. Imported XMLs remain unsigned, local and separately sourced; no automatic upload or repair is performed.

## Preview.110 — independently compare Computer and User gpresult XML with all DC GPOs

From **GPO Operations → Conflicts → Scan all DC GPOs**, open **Compare gpresult XML...**. This is a separate window from the existing **Analyze effective (PC)** XML importer. The former covers **both** Computer/Lock Screen/power and User/Screen Saver sources; the latter remains computer-only and is unchanged.

1. On a representative Windows 10/11 endpoint, run `gpresult /scope computer /x "C:\Reports\gpresult-computer.xml" /f` from an authorized elevated command prompt. For a representative **signed-in user**, run `gpresult /scope user /x "C:\Reports\gpresult-user.xml" /f` in that user's suitable session. Access privileges and XML completeness may vary.
2. Transfer XML files securely to the operator computer; **do not upload them publicly**. Select the exact Computer FQDN and/or `DOMAIN\username` or a user principal name (UPN) that the report actually contains. The dialog cannot independently verify the typed account against AD.
3. Use **Import Computer XML...** and **Import User XML...** independently. Each local import asks for privacy confirmation, validates the XML's expected identity, scope, log timestamp, GPO GUIDs and application flags, then keeps only bounded parsed evidence and SHA-256 in memory. The original XML is never copied or uploaded.
4. A snapshot older than 24 hours requires separate confirmation; imported times and hashes appear in the detail pane. Changing either typed identity automatically clears only the corresponding imported snapshot. The other scope remains independent.
5. Filter by **Computer/User** or XML observation, then select a row. **Reported applied** and **Reported filtered** are unsigned XML observations about the *GPO GUID in that scope*, **not** proof that the **configured parameter** won. **Not listed (unknown)** and **Insufficient flags** cannot be treated as policy exclusion; missing User/Computer imports are **Not imported**.

The selected DC's scanned SYSVOL sources and imported client files may be from different replication versions and different times. User XML and Computer XML can reflect **different endpoints/sessions**; no client association or winner is inferred. Security filters, loopback, WMI, MDM, session type, active power plan, actual screen-off and physical lock display require a real endpoint pilot. The scanned GPO set remains a subset of relevant registry/GPP/CSE settings, not a complete semantic index of every Group Policy extension.

# RSoP Evidence Import — 0.4.0-preview.24

**Status:** Preview, offline XML import only. No changes to AD, GPO, SYSVOL, Windows clients or configured user credentials. There is no background polling, remote PowerShell, gpupdate, automatic collection or Git upload.

## Generate report on the actual Windows 11 computer

Open an elevated Command Prompt **on the target computer** (with authorization to read computer-scope RSoP), then run:

```cmd
mkdir C:\Reports
gpresult /scope computer /x "C:\Reports\gpresult-computer.xml" /f
```

This is the native Windows command; no RSAT is required. It saves XML with computer-scope results. Microsoft documents the `/scope computer`, `/x` and `/f` options at [Microsoft Learn — gpresult](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/gpresult).

Copy the resulting XML securely to the administrator computer. **Do not** send raw XML to GitHub or outside the organization: it may expose account names, domain identities, group membership, GP paths and internal settings. The manager never generates this file remotely or instructs you to enable Remote Registry.

## Import in LockScreen Campaign Manager

1. Open **GPO Operations → GPO: Disable / Reuse / Delete** and select the existing managed campaign GPO.
2. Open **Lock Screen conflicts**, enter the **full AD computer FQDN** in the computer field and choose **Analyze effective (PC)**.
3. The manager first verifies the computer belongs to the selected **OU and security group** and runs the existing **read-only** candidate analysis on the selected DC.
4. In the **Effective GPO candidates** window select **Import gpresult XML**. Choose a regular **local** `.xml` report, inspect the privacy warning and confirm.
5. The report is checked for the expected XML namespace, logged computer-scope data, matching computer and domain, unambiguous read timestamp, GPO GUIDs and data limits. A snapshot older than 24 hours is labelled **Historical** and requires a second confirmation.
6. Compare the **AD Assessment** and **Imported RSoP** columns. Select a GPO to read the client-side observation and source-time/hash. Use **RSoP-only GPOs** to inspect imported policies not found in the AD candidate list, or **Clear imported RSoP** to discard the snapshot from the open window.
7. Optional **Export visible CSV** includes the current candidate list, imported XML GPO flags (when present), client read time and SHA-256. It is local only, opt-in and escapes spreadsheet formulas.

## Interpret cautiously

| Imported RSoP observation | Meaning |
| --- | --- |
| Reported applied | Computer's gpresult/GPO record has `Enabled=true`, `IsValid=true`, `FilterAllowed=true`, `AccessDenied=false` |
| Reported filtered | At least one explicit enabled/valid/filter/denied flag indicates that the client excluded the GPO |
| Insufficient flags | Not all required computer-scope flags are present, so a positive conclusion is blocked |
| Not listed (unknown) | GUID is absent from the imported computer GPO list; **not** evidence that it does not apply |
| Not imported | No local XML has been manually supplied for this report |

Only a GPO GUID is accepted for matching. A name match **never** establishes identity. User-only GPOs are ignored for computer-scope analysis. Some XMLs can list an unidentifiable `Local Group Policy` or other entry without a GUID: those count as **unidentified**, not misattributed to a similarly named domain GPO.

**Neither a reported-applied GPO nor a successful import proves the campaign's lock screen image currently appears.** The XML is an unsigned user-supplied point-in-time snapshot; subsequent GPO changes, client-side extension failures, WMI results, GPP item-level targeting and per-setting precedence require additional checks. An absent candidate is not a proven denial. The manager deliberately does not parse all CSE settings or claim to know the winning value of LockScreenImage.

## Import security

- XML file only (no HTML or text fallback), bounded to 12 MiB on a local volume. UNC/device paths, file reparse points and linked parent folders are refused.
- XML DTD and external entities are disabled; parsed character limits are enforced; the importer never executes scripts or external references from the file.
- The imported report's source identity must match the AD-verified computer. Contradictory/duplicate GUIDs, invalid timestamps and a foreign computer/domain are rejected.
- Only the parsed evidence stays in memory. The original file is neither copied nor saved automatically. Closing the window discards the import. CSV export requires separate explicit approval.
- Read timestamp and SHA-256 are included to explain what was observed. SHA-256 detects accidental changes **but does not authenticate** that a report came from the intended computer.

## CI vs live acceptance

Windows hosted Actions tests parse XML with a synthetic gpresult-like fixture, verify applied/filtered/missing/old/wrong-PC/XXE rejection, test SHA-256, build the standalone x86/x64 EXEs and smoke the DPI-aware import/clear/export buttons. Hosted CI cannot validate a real AD/Windows 11 RSoP report; acceptance on a real domain remains deferred as agreed.

**Application, agent and updater:** 0.4.0-preview.24. Stable release is unchanged.
