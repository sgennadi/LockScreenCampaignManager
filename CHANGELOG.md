## 0.4.0-preview.103 — translated OU/User/Computer group targeting (2026-11-12)

- Localize AD Deployment OU-first and group-first choices, direct-membership results, connection warnings, GPO link guidance and User/Computer assignment buttons into Russian/Hebrew. Do not change LDAP distinguished names, group identities, GPO rights or security filtering.
- Use reversible UI-only ComboBox formatting for the two group-list modes. Keep SelectedIndex and the original English command/serialization values, even when switching English ↔ Russian ↔ Hebrew.
- Localize independent Screen Saver (User OU + User Group) and Lock Screen (Computer OU + Computer Group) editing instructions, including notices for group-first selection and nested/primary-group limitations.
- Improve assignment-window DPI resilience: shorten visible group filters, preserve full descriptions in tooltips, and auto-wrap long operator guidance and titles in 100–200% WinForms layouts.
- Add offline locale-key, reversible-value and format-placeholder contracts plus x86/x64 WinForms scale tests and a concise numbered What's New. Real physical 200% and live AD/RSoP acceptance remain open (#36, #30, #25/#26).

## 0.4.0-preview.102 — native 200% display / EN-RU-HE visual acceptance (2026-11-12)

- Add native **Tools / Help → Visual DPI / language check** in the portable EXE with a scrollable six-item checklist for real UI inspection (Wizard, AD Browse, Client Health, RTL, mixed technical LTR and English/Russian/Hebrew switching).
- Read effective DPI of the check window with Windows GetDpiForWindow (PerMonitorV2), verify a local console and exactly 192 DPI (200%); remote desktop or simulated DPI never qualifies for local acceptance.
- Allow explicit offline `--ui-smoke-test` execution with a bounded subprocess timeout, then gate local report saving on smoke success and all six checked physical observations.
- Store unsigned operator evidence only in LocalAppData with no machine/domain identity, screenshots, remote commands, network upload or GPO operations; never mistake it for cryptographic attestation or real policy results.
- Add reversible Russian/Hebrew captions, WinForms simulated-scale smoke, native selection/acceptance unit assertions and offline source/privacy CI tests. Include operator guide in the Lab Kit. Real physical checks still require a Windows desktop (#36).

## 0.4.0-preview.101 — preserve group-first OU/Group targeting (2026-11-12)

- Preserve explicitly chosen Computer and User security groups when selecting/changing their target OU, instead of allowing direct-member LDAP filter refresh to clear the selected DN.
- If a chosen group does not appear in an OU's direct memberOf results, switch visibly to All groups and show a warning about nested/primary membership and Preview verification; no silent deletion of group assignment.
- Restore independently saved Computer/User group DNs when changing assignment rows; preserve a company's saved group default rather than losing it during first OU selection.
- Add model, WinForms matrix and offline source-contract regression checks for two independent scopes, case-insensitive DNs and group-first operation.
- Retain Preview.100 read-only LDAP, mandatory OU/GPO Preview, explicit deployment approval and verified ZIP/update behavior. Real AD membership/physical DPI 200% and JPG User GPO pilot remain open.

## 0.4.0-preview.100 — OU/member-group targeting and AD deployment Browse (2026-11-11)

- Selecting an OU in AD Deployment now offers read-only groups containing directly assigned computer accounts from that OU and descendants, using signed/sealed native LDAP memberOf. The group object may live in any OU.
- All domain security groups remains available for group-first selection; an actual OU to link the GPO is still mandatory and independently verified before Deploy.
- Screen Saver uses User OU and User Group selection with its own direct-user membership filter in the group matrix. Both continues to use independently scoped User and Computer GPOs; no implicit loopback.
- Show parent OU paths to distinguish same-name OUs, de-duplicate exact DN entries and move SMB Browse into a non-clipped full-width row.
- Add offline source/safety and multi-scale WinForms regressions; fail closed on incomplete OU membership results and preserve all Preview.99 updater and deployment protections. Physical DPI and real AD applicability checks remain open (#36, #30, #25/#26).

## 0.4.0-preview.99 — Russian/Hebrew Repair results and GPO selection guidance (2026-11-11)

- Translate Repair completion summaries, counters and confirmation dialogs in Russian and Hebrew with reversible English fallback. Original numeric result values and their placeholder mapping are preserved.
- Localize the warning that a remote Repair operation may have executed although the result queue failed to save, without implying safe automatic retry.
- Translate missing managed-GPO selection guidance for GPO lifecycle and remote-task Preview.
- Add layout smoke and offline contract tests that round-trip all repaired captions and verify count placeholders remain intact at six simulated Windows font/DPI profiles.
- Keep Preview.98 numbered What's New and portable updater safety intact; no change to AD/GPO/SMB/RPC execution, repair queue/history schemas or install approval.
- Real Windows 10/11 200% interactive RTL/LTR, SMB LocalSystem startup and JPG User Screen Saver GPO acceptance remain open (#36, #30, #25/#26).

## 0.4.0-preview.98 — compact numbered What's New and updater display (2026-11-11)

- Show a five-item, plain-text, numbered What's New summary at the top of Updater results; retain installed version, package filename, size and raw SHA-256 underneath.
- Accept numbered/bulleted release notes and summarize older paragraph-based notes, without displaying raw Markdown headings, bold markers or inline link destinations.
- Make the update-notification tooltip show up to three concise numbered changes instead of copying the first raw Markdown lines.
- Translate the What's New heading and empty-note state into Russian/Hebrew; preserve original release notes for the separate GitHub page.
- Add offline self-tests and source contracts for parsing, localization, input length caps and release notes structure. This is only a UI change and does not change GitHub download, signature checks, manual install approval or rollback.
- Windows x86/x64 CI and offline lab checks are required before publication. Real Windows 10/11 200% DPI, SMB LocalSystem and JPG User GPO pilot acceptance remain open (#36, #30, #25/#26).

## 0.4.0-preview.97 — Russian/Hebrew GPO safety and Client Health warnings (2026-11-10)

- Add over 80 new display strings per language for Client Health SMB browsing/repair/final-report confirmation, read-only RSoP XML and remote HKLM verification, GPO candidate report export, Campaign Closure Audit, inventory counts and manual remote task status.
- Operator-facing read-only RSoP/HKLM/SMB prompts and local confidential CSV export consent now use English/Russian/Hebrew, retaining **No** as the default. Verified GPO/OU/group scope and per-operation revalidation remain mandatory, as do exact GPO GUID confirmation and fail-closed state transitions.
- Client Health feedback messages and warnings localize but do not alter UNC source values, actual SMB ACL, JSON report schema, original CSV column names, agent status identifiers or reports. Exporting a final report still requires the same explicit confirmation when any clients remain unresolved.
- Add six-profile simulated 100–200% WinForms smoke and roundtrip checks for Client Health labels/technical paths and source-level privacy/consent contracts. The tests are offline only and do not contact AD, RPC, SMB or remote Registry.
- Localization is **not yet complete** (#36); real Windows 10/11 200% Hebrew RTL visual review, SMB LocalSystem startup/cleanup (#30) and automatic JPG User GPO pilot (#25/#26) remain open.

## 0.4.0-preview.96 — Bilingual updater / Local Lab evidence and DPI contracts (2026-11-10)

- Add 77 new Russian and 77 new Hebrew presentation-only captions for the Updater, package verification, signed-publisher/rollback/backup warnings, and local Windows Lab Evidence viewer. Reuse already translated common words where available.
- Localize Updater read-only Check, verification, progress, install approval and rollback inspection. **No automatic install**: a verified package still requires a separate explicit Yes (No by default); no update is attempted against administrator-owned or running gMSA copies.
- Keep raw release notes, SHA-256, installed version, GitHub repository, package architecture, token, check results and JSON evidence unmodified. The Preview/Stable picker is display-only; SelectedItem and SelectedIndex remain canonical.
- Localize local lab import/rejection/status labels while keeping untrusted JSON report evidence and local-only validation intact.
- Extend WinForms simulated DPI 100–200% LTR/RTL switching tests, text-height and localized ComboBox width checks. Interactive Windows 10/11 physical 200% DPI and live domain acceptance still pending (#36). SMB boot and JPG User GPO testing remain open (#30/#25/#26).

## 0.4.0-preview.95 — Trilingual Advanced UI and Device/Repair dashboard (2026-11-10)

- Expand English/Русский/עברית presentation to native gMSA 24/7 setup, read-only readiness, SMB feedback settings, Client Health alerts and Device & Repair Dashboard. Add over 100 translated operator captions and notices per language, without changing protocol or policy data.
- Translate gMSA installation, disable, task removal and Repair queue cancellation warnings. All original permissions and confirmation gates remain, with **No** as default. Do not change stored machine queue, worker or GPO operation IDs.
- Translate readiness UI table headers and operator counts while keeping the original check results, raw evidence, file paths, GUIDs and privacy-safe exported JSON unchanged.
- Translate verified AD fleet totals, dashboard errors and manual Repair warnings while preserving selected device filters and the existing Preview + complete GPO GUID approval before RPC.
- Translate feedback folder/ACL warnings and Client Health alert descriptions while retaining exact UNC strings, actual filesystem ACL logic, serialized health reports and alert settings.
- Add six-profile 100–200% simulated WinForms tests (Russian LTR → Hebrew RTL → English LTR), immutable values and improved translated action-button sizing. Real Windows 10/11 interactive DPI acceptance still open (#36). SMB startup and JPG User GPO pilot gates remain open (#30/#25/#26).

## 0.4.0-preview.94 — Russian LTR, expanded Hebrew scheduler and Advanced UI (2026-11-10)

- Add **Русский (Russian)** as a third persisted Interface Settings language alongside English and עברית. Russian uses normal LTR; Hebrew keeps RTL. Switching among all three languages and returning to English preserves selected campaign/GPO modes and technical identities.
- Create a Russian presentation dictionary with 690+ localized strings covering the main editor, seven-step Campaign Wizard, AD connection and user selection, SYSVOL/SMB choice, PS1/EXE Deployment, Client Health, GPO Operations, Deferred Repair/gMSA, diagnostics, schedule, company profiles, assignment matrix, history and advanced read-only views. Unknown labels intentionally fall back to English; this is **not** 100% localization.
- Expand Hebrew localization to the independent Campaign Schedule editor, image-folder matching, company profiles, group assignments, history, several advanced GPO analysis dialogs and Client Health alerts. UTC/timezone ID, computer and domain paths, GIF/JPG file names, JSON report bodies and GUID values remain LTR.
- Normalize UI captions through canonical English keys, preserving original control and grid-header meaning when switching Russian → Hebrew → English in one open window; provide display-only formatting for mode, source, group and filter ComboBoxes. Locale switching never changes SelectedIndex, persisted JSON, GPO operation IDs or remote commands.
- Auto-grow translated action buttons and keep scrollable advanced pages usable with simulated 100–200% DPI/font profiles. Add both WinForms and Python regression checks, without network/AD/GPO/SYSVOL writes.
- Real interactive Win10/11 Hebrew RTL/Russian 200% visual QA, untranslated dynamic/advanced messages, SMB startup/cleanup and JPG Screen Saver User GPO pilot acceptance remain open (#36, #30, #25/#26).

## 0.4.0-preview.93 — Hebrew GPO Operations, diagnostics and conflict reports (2026-11-10)

- Extend reversible English/Hebrew operator-facing translations to GPO Operations, Agent Management filters, read-only conflict report, Deferred Repair and gMSA background Repair queues, diagnostic report review and offline error trends.
- Translate GPO action and queue/risk filters **only for display**, leaving GPO Lifecycle IDs (Disable, Enable, Unlink, Relink, Reuse, Delete), remote Push/Repair/Cleanup/Report codes, AD activity filter keys, gMSA queue states, risk selection and their selected indices unchanged.
- Add display-only localized headers to Deferred Repair, gMSA, read-only conflict and diagnostics trend tables. **Do not change** the exported diagnostic JSON fingerprints, GroupConflict CSV headers, report row values, queue storage, GPO preview hashes or reviewed privacy warnings.
- Keep raw AD/GPO permissions evidence, GUID confirmation entry, domain/computer DNs, technical diagnostic payloads, JSON and path data left-to-right; preserve explicit Preview, GUID and local private CSV confirmation. Label placeholders and public GitHub privacy warning now translate reversibly.
- Add six-profile (100–200% simulated) WinForms tests for Hebrew/English form titles, gMSA/Deferred tabs, column-header reversal and canonical operations/filter values; add Python static invariants for unmodified CSV/JSON and GPO commands.
- The interface is **not yet 100% Hebrew**. Advanced validation messages, raw technical diagnostics, dedicated schedule editor and some additional windows remain English. Actual interactive Windows 10/11 RTL at 200% DPI is still open (#36); no live domain, SMB endpoint or JPG User GPO readiness is claimed (#25/#26/#30).

## 0.4.0-preview.92 — Hebrew AD/GPO and Client Health UI (2026-11-10)

- Extend the reversible English/Hebrew presentation layer to the AD Connection Wizard, alternate-account/password picker, AD/GPO deployment, and Client Health / Live. Translate commonly used headings, actions, status hints, read-only permissions explanations, and SMB feedback settings.
- Translate **display labels only** in the deployment's PowerShell/EXE and SYSVOL/SMB selectors via ComboBox formatting, preserving SelectedIndex and original strings used for campaign settings. Keep DC/domain names, selected OU/group identities, UNC paths, AD credentials, GPO preview contents, evidence and diagnostic exports unchanged.
- Translate Client Health column headers for display only, restoring their original English captions when switching back. CSV field names, JSON report schema, computer names, raw status codes and snapshots are unchanged; UI alerts and aggregate counts use localized presentation strings.
- Preserve LTR for technical fields and AD/ACL evidence in Hebrew RTL windows; improve action button sizing and add offline WinForms 100-200% scale smoke tests for language reversibility, selected mode/source stability and grid headers.
- GUI localization is **still partial**: all underlying protocol/debug/PowerShell output remains English, and some contextual or exception messages are not translated. Real interactive Windows 10/11 RTL and 200% DPI acceptance is still open (#36), as are SMB endpoint acceptance (#30) and JPG User GPO rollout gates (#25/#26).

## 0.4.0-preview.91 — Seven-step Hebrew campaign Wizard and RTL layout safety (2026-11-10)

- Extend English/Hebrew presentation strings across the seven-step Campaign Wizard: connection, campaign type, image selection, ordering, schedule, Save/Skip and Deploy. Include JPG Photo Screen Saver choices, separate SYSVOL/SMB source labels, source/ACL warnings and saved-project status. Untranslated technical details remain English.
- Keep campaign-type enum and SYSVOL/SMB ComboBox source items unchanged; translate only their displayed text via WinForms formatting events. Preserve selected index, UNC path direction, technical JSON, AD/GPO scope and existing campaign settings while switching language.
- Reversibly localize active wizard rail, step subtitles and Screen Saver skip markers. Use auto-growing action captions and scrollable page text rows at high DPI instead of cutting off translated descriptions.
- Expand offline Windows UI smoke checks for all seven pages in Hebrew and English, plus Python source contracts. Neither test writes to AD/GPO/SMB or changes saved language preferences. Real interactive 200% DPI/RTL review is still pending.
- No automatic JPG Screen Saver User GPO rollout; no claims about production AD/Windows 10/11 endpoint success. See issues #25, #26, #30 and #36.

## 0.4.0-preview.90 — Initial Hebrew/RTL UI and local lab evidence reader (2026-11-10)

- Add **English / עברית (Hebrew)** to Interface settings (saved to existing per-user UI preferences JSON) and apply a central Hebrew dictionary to primary navigation, buttons, wizard actions, updater, GPO operations, and new evidence viewer. Enable right-to-left form layout for Hebrew while preserving LTR technical data, UNC paths, GUIDs, hash/JSON and troubleshooting logs. Dynamic/non-dictionary text intentionally remains English until fully localized.
- Revise Interface settings as a high-DPI scrollable panel, preserving density, UI scale and font scale selections. Verify Hebrew/English switching at 100–200% simulated scale without saving CI preferences.
- Add a **local-only, read-only Lab evidence** pane to main Tools and GPO Operations; import existing `lscm-smb-system-canary/v1` or `lscm-endpoint-closure/v1` JSON reports individually with max 256KB file size, strict schemas, timestamp/boolean checks, unknown result rejection, 24h stale indicator and explicit unsigned evidence disclaimer. Do not read AD, connect SMB, execute clients or upload reports.
- Strict import tests reject success-result contradictions, future timestamps, private/raw finding text and unsupported schemas. No claim that on-screen display or remote device status is verified. Existing JPG Photo Screen Saver User GPO is still blocked pending live acceptance.

## 0.4.0-preview.89 — Clear SMB permissions evidence in GPO Preview (2026-11-10)

- For SMB content, omit unrelated Smart SYSVOL duplicate-image audit; preserve it for SYSVOL content and label the storage comparison accordingly.
- Report exact validated SMB UNC root, unique immutable campaign folder and startup script path in deployment Preview. Mark NTFS broad-write Allow-ACE scan **PASS (ADVISORY)**, and separately mark share permissions, computer-account READ and startup SMB connectivity as **NOT TESTED**. No preview can claim live endpoint success from an admin's read access.
- Fail closed for inconsistent/missing read-only Plan evidence fields; apply the same storage evidence contract to single-target and per-group Lock Screen preflight, and show exact UNC and unresolved access limits in final GPO approval prompts.
- Add pure offline self-tests and CI contracts to verify the distinction and preserve the automatic JPG Photo Screen Saver User GPO gate. Real Windows 10/11 domain LocalSystem tests are still outstanding.

## 0.4.0-preview.88 — Source selection before project save, no silent stale JSON (2026-11-10)

- Share a single SYSVOL/SMB storage selector between Campaign Wizard Step 6 (Save / Skip) and Step 7 (Deploy), including the existing editable UNC and Browse controls. This prevents saving the campaign before the storage choice is presented.
- Mark in-memory campaigns dirty when the source or network folder changes. Changing storage after a previously saved project flags its JSON as **outdated**; add explicit **Save project changes...** action on Deploy, and require a warning/confirmation before finishing or opening AD deployment without saving again.
- Keep **Skip saving & continue** an intentional supported option; don't turn it into an implicit save or a forced GPO action. Changes made in the nested AD/GPO Wizard are also reconciled with the editor's save-state indicator.
- Extend Windows x64/x86 WinForms 100–200% scale smoke checks to require source selector and UNC editor on **both Save and Deploy pages**. No AD/GPO/SYSVOL/SMB writes during UI editing.
- Real-domain Lock Screen/SMB boot acceptance and automatic JPG Photo Screen Saver User GPO remain gated separately.

## 0.4.0-preview.87 — Wizard storage selector and Updater HiDPI field fix (2026-11-10)

- Expose **Campaign files: SYSVOL / SMB network folder (UNC)** on the main seven-step Campaign Wizard **Step 7 Deploy**, not only in the separate AD deployment wizard. Type or Browse for the existing share; a rejected UNC stays local with an actionable warning. No GPO write or server connection occurs merely from changing the field.
- Carry the chosen storage mode and UNC root from the editor through the deployment copy and back again; existing projects default to SYSVOL.
- Size the Updater **Release channel** label, native ComboBox and other fields from font and preferred control heights instead of fixed 24px label / 50% rows. Refit on DPI/font changes; preserve the two-step Check / Install interaction.
- Expand Windows UI smoke tests at 100%, 125%, 150%, 175%, scaled-font and 200% profiles for the full `Preview + stable` text, UNC Browse visibility, and Wizard project persistence.
- No AD/SYSVOL/SMB writes during UI selection. JPG Screen Saver automatic User GPO remains blocked pending real domain acceptance.

## 0.4.0-preview.86 — Independent endpoint closure acceptance (2026-11-10)

- Add explicit two-stage **Baseline before GPO creation / VerifyCleanup after unlink** LocalSystem acceptance for one isolated Windows 10/11 computer (SYSVOL or SMB). VerifyCleanup independently compares the original registry SHA-256 to actual HKLM policy, local tasks/files/ownership, final agent postcleanup receipt and exact Computer GPO GUID absence in local RSoP.
- Do not mistake client `Completed` feedback for verified cleanup. Baseline is immutable and excludes prior completed campaign IDs; an incomplete registry, task, RSoP, owner or receipt check fails closed. No settings, GPO, SYSVOL or SMB files are modified by the probe.
- A temporary LOCAL SYSTEM Scheduled Task is removed after inspection. A private evidence directory keeps baseline metadata and sanitized reports while temporary raw GPRESULT XML is deleted. No automatic uploads or remote execution.
- Include `run_endpoint_closure_local.ps1`, `endpoint_closure_probe.ps1` and instructions in the verified offline Windows lab kit. Add PS 5.1 parsing, CI source contracts and explicit non-SYSTEM rejection tests on x64/x86.
- Does NOT satisfy real Windows 10/11 domain acceptance or visual JPG Screen Saver acceptance. Production native EXE and JPG User GPO still gated.

## 0.4.0-preview.85 — RSoP GPO GUID / post-deployment reboot evidence (2026-11-10)

- Add completed deployment UTC and exact GPO ID to the read-only LocalSystem canary input so a *disposable* Windows 10/11 client can report whether it has rebooted after GPO creation.
- Generate temporary protected `gpresult /scope computer /x` XML, match only the expected **ComputerResults GPO Path GUID**, and fail closed for filtered, disabled, missing or duplicated policies. Raw XML is removed and never uploaded. An applied GPO entry is *not* proof of a winning setting.
- Keep source manifest, agent and image SHA-256 validation, cache integrity and no-AD-mutation guarantees from Preview.84. Add Windows PowerShell 5.1 RSoP parser fixtures and revised offline lab kit.
- Real GPO Startup execution, visible Lock Screen and rollback still require dedicated domain endpoint acceptance. JPG Photo Screen Saver User GPO remains gated.

## 0.4.0-preview.84 — LocalSystem SMB canary and pinned deployment receipt (2026-11-10)

- Confirm exact campaign.json and PS1/EXE agent SHA-256 in native read-only preflight and reverify the complete SMB package before GPO creation and startup registration; save evidence in successful deployment receipts.
- Package opt-in local-only SYSTEM Scheduled Task canary for a disposable Windows 10/11 machine. The read-only probe verifies SMB machine-account reachability, pinned manifest, agent and all JPEG images, and any pre-existing local cache. It never executes source scripts or changes AD/GPO/SYSVOL.
- Protect local diagnostic reports, remove the temporary task and input configuration, and never upload data automatically. Probe cannot prove actual GPO application, boot-time availability, lock-screen display or rollback; those remain manual acceptance gates.
- Add parser and source-contract regression tests to Windows x64/x86 CI and include the canary in the offline-lab ZIP. See [SMB SYSTEM canary](docs/SMB-SYSTEM-CANARY.md).

## 0.4.0-preview.83 — Optional SMB network campaign source (2026-11-09)

- Add **Campaign files: SYSVOL (default) / SMB network folder (UNC)** to the AD/GPO deployment Wizard. The selection persists in campaign projects, invalidates stale Preview evidence, and applies to single computer groups and per-group Lock Screen packages.
- Keep the GPO AD/SYSVOL GPT, user/computer filtering and Startup registration unchanged; publish the immutable Lock Screen agent, campaign manifest and JPEGs to an **existing dedicated SMB folder** when selected. Both PS1 and restricted EXE client modes use the same source selection.
- Fail closed on unsafe/linked UNC content roots, SYSVOL/NETLOGON, existing campaign folders, broad NTFS write rights, invalid paths, broken hashes or changed scope. Recheck source security immediately before staging and GPO link. No share is created automatically; computer-account SMB read permission and real startup behavior require pilot verification.
- Preserve the published original SYSVOL mode for older campaigns. Existing GPO Operations SYSVOL cleanup does **not** remove external SMB files; remove them manually only after GPO unlink and verified endpoint cleanup.
- Add package-model self-tests, multi-DPI Wizard selector smoke, offline safety tests and Windows x86/x64 release checks. JPG Photo Screen Saver automatic User GPO activation remains blocked pending isolated Windows/AD acceptance. See [SMB deployment guide](docs/SMB-CAMPAIGN-SOURCE.md).

## 0.4.0-preview.82 — JPG Screen Saver manual Computer GPO pilot kit (2026-11-09)

- Add **Export GPO lab kit...** to Wizard Save for JPG Screen Saver / Both, after first creating the hash-verified .scr package. Keep built-in .scr User GPO and Lock Screen agent behavior unchanged.
- Export `PhotoSaver-StartupInstall.ps1`, identical pinned .scr + manifest + JPGs, and human-readable GPMC lab directions as one atomic, non-overwriting, local-only package. Bind the script to exact campaign ID, slide interval, and SCR/manifest SHA-256.
- New Windows PowerShell 5.1 Computer Startup lab installer runs only as LocalSystem, verifies JPG hashes and protected local ACL, and never edits AD/GPO, users' HKCU, a local Screen Saver registry key, or SYSVOL.
- User GPO activation is still **blocked**. Do not point a user policy to a file that has not been installed on every eligible computer. Manual testing must verify SCR behavior, policy scope, AppLocker/WDAC, rollback and effective gpresult.
- Add Windows x86/x64 full build, offline contracts, PowerShell 5.1 syntax, immutable export fixture, and simulated DPI Wizard checks. Version increments to preview.82 without touching production client agent versions.

## 0.4.0-preview.81 — Explicit current-user JPG Screen Saver pilot install / rollback (2026-11-09)

- Extend the Campaign Wizard Save page with opt-in **Install local JPG pilot...** and **Remove local JPG pilot...** actions after exporting a photo .scr package. Require explicit confirmation (default No).
- Verify the exported .scr and JPG SHA-256 manifest, copy verified files into current-user LocalAppData, back up the four previous HKCU Desktop screensaver settings before changing anything, then set the local .scr program, idle timeout and password-on-resume.
- Reject installation when HKCU/HKLM Screen Saver policies are present, never elevate, write to AD/GPO/SYSVOL or activate arbitrary executables. No production domain deployment is enabled by this feature.
- Remove restores the exact prior current-user registry values only if all four current values still equal this pilot's installed settings. If policy or the user changed anything afterward, refuse a destructive/stale automatic restore.
- Add simulated 100-200% DPI Wizard checks and offline source-contract tests. Hosted Windows CI verifies builds, but a real Windows 10/11 user desktop pilot is still required for idle activation, /p preview, multi-monitor and unlock behavior.

## 0.4.0-preview.80 — JPG Screen Saver standalone pilot package (2026-11-09)

- Add a separate **JPG slideshow** option under Campaign Type > Screen Saver. When enabled, the Wizard does NOT skip Images, Arrange and Schedule; existing built-in `.scr` mode still skips these steps. Support multiple files or a folder, with 5–3600 second JPG transition interval.
- A new **Export JPG .scr pilot...** action creates a portable, local-only `PhotoScreenSaver.scr` package from the current signed/pinned single-file Manager apphost, its `slideshow.json`, and normalized JPEGs. Verify the binary, each JPEG and manifest by SHA-256 and reject linked/unsafe sources.
- The renamed SCR enters a tightly restricted screen saver command parser before client/system/AD/updater commands: `/s` fullscreen JPG slideshow, `/p` preview, `/c` informational settings, and offline `--photo-saver-self-test`. Mouse/keyboard input exits fullscreen; screen saver does not create background tasks or change the user's Windows configuration.
- Expand group-assignment editor with JPG mode and slide interval. Save JPG settings with the project without changing older built-in Screen Saver campaigns.
- **Safety gate:** automatic User GPO delivery of the JPG runtime is blocked and no existing GPO is modified. Validate the SCR process, Windows screensaver invocation, multi-monitor behavior and client security on an isolated Windows 10/11 machine before adding domain-wide SYSVOL delivery. Pilot is not a production-ready domain rollout.
- Add Windows CI smoke tests for JPG Wizard skip transitions and SCR package export/integrity. Bump manager to preview.80 and existing x64/x86 release automation; production Lock Screen PS1/EXE agents remain unchanged.

## 0.4.0-preview.79 — Complete per-target GPO candidate report (2026-11-09)

- Add **Full conflict report...** to Group Assignments Preview. Its read-only DataGridView displays the complete (up to 500 per target) static policy candidates instead of truncating to the 12-row compact text Preview, with User/Computer scope, GPO identity, source and selected setting details.
- Search across target, policy, setting, OU/link and values; filter by High/Warning/Info/Inactive. When a target has no matching static hits, show coverage notes and clearly state that zero hits is not proof of effective RSoP.
- Add explicit-approval, **local-only CSV export** of the filtered table; escape CSV quotes, newlines and leading spreadsheet formula operators. Never upload internal domain/OU/GPO details to GitHub automatically.
- Validate native scanner `count`, `highRisk`, and `warningsCount` against every returned hit before accepting the pre-deploy report. A count mismatch aborts Preview rather than silently losing data.
- Cache the complete report only for the approved Preview snapshot; clear it after changes to OU, group or campaign settings. The pre-deployment GPO authorization rules are unchanged.
- Add multi-scale WinForms smoke and a 25-finding CSV truncation/formula-escaping regression test. Bump manager version and automated x64/x86 release workflow. Endpoint client binaries remain unchanged.

## 0.4.0-preview.78 — Strict MS-GPREG GPO conflict evidence (2026-11-09)

- Harden native `Machine/Registry.pol` and `User/Registry.pol` read-only conflict scanning: keys and value identifiers must end with UTF-16LE NUL followed by a separate semicolon. Previously the scanner accepted the semicolon alone and silently discarded NULs, so malformed registry policy evidence could be mistaken for valid policy settings.
- Check bounded UTF-16LE data lengths and required string terminators; reject invalid REG_DWORD/REG_DWORD_BIG_ENDIAN/QWORD sizes and invalid Unicode text. Preserve binary/unsupported entries as candidate findings rather than claiming to know their value.
- Fix the computer-scope self-test `Registry.pol` fixture to include actual MS-GPREG NUL terminators. Add negative tests for a missing key NUL, missing value NUL, malformed DWORD and unterminated REG_SZ, in addition to the already-valid User Screen Saver fixture.
- Preserve read-only, fail-closed pre-deployment behavior. Malformed linked policies now stop Preview and Deploy; operators should inspect existing GPOs with GPMC/gpresult and compare with selected DC SYSVOL. No GPO/AD repair, client PS1 or EXE changes are included.
- Bump manager to `0.4.0-preview.78` and release automation for separately verified x64/x86 and updater ZIP packages. Real AD and endpoint RSoP testing remains an isolated pilot task.

## 0.4.0-preview.77 — Correct GPO empty-half handling and Screen Saver policy path (2026-11-09)

- Fix a false-blocking Preview.76 regression: a linked GPO with no User/Computer settings can legitimately omit the respective SYSVOL subdirectory. Before rejecting missing policy folders, inspect AD GPC `versionNumber` and `gPCUserExtensionNames` / `gPCMachineExtensionNames` on the **same selected DC**. Skip only an unconfigured policy half (with coverage note); reject missing User/Machine folders if AD says the half contains settings.
- Fail closed when the registry policy CSE is advertised for an applicable GPO but its `Registry.pol` is missing. Keep rejecting access denied, UNC/network errors and unsafe junction/reparse folders for GPP evidence, never equating them with an empty policy.
- Detect `NoDispScrSavPage` at the correct Microsoft ADMX `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System` registry key, and explain that this user UI restriction does not itself disable Screen Saver execution.
- Add self-tests for User/Computer GPO version halves, signed 32-bit version values, Registry CSE metadata, correct HKCU GPP path, and negative tests for accidental wrong-hive/wrong-key detection.
- Update manager to preview.77, publication workflow and release documentation. Existing PS1 and native EXE agent payloads are unchanged; live AD/RSoP/Client tests still require a dedicated pilot.

## 0.4.0-preview.76 — Scoped read-only User/Computer GPO conflict auditing (2026-11-09)

- Extend the native read-only GPO conflict scanner with an explicit **User** policy scope, while keeping existing **Computer** as the backwards-compatible default. Read the matching SYSVOL User/Machine halves with selected-DC LDAP identity and signed/sealed connections.
- Detect Screen Saver HKCU `ScreenSaveActive`, `SCRNSAVE.EXE`, `ScreenSaveTimeOut`, `ScreenSaverIsSecure` and `NoDispScrSavPage` in `Registry.pol` and GPP Registry XML; do not confuse HKLM policies with HKCU.
- Each row in group-targeted Preview now separately audits its Computer or User OU-linked and inherited policies, reports per-target counts/setting samples/coverage warnings, and fails closed if scan fails or scope is mismatched.
- Deploy repeats every audit before approval. High-risk or warning static overlaps require a second explicit default-No decision before the existing confirmation. A zero-hit audit is never reported as effective RSoP.
- New offline test fixtures cover GPO User/Computer disabled-policy bits, all four built-in Screen Saver registry values, HKCU-vs-HKLM GPP scope, malformed user policies, and mis-typed scope; CI checks the full group-preflight contract.
- The production PowerShell and lab-only EXE **agent code remains unchanged** at 0.4.0-preview.75; this is a manager-side preflight update with no AD writes in its checks. Actual user gpresult, WMI, MDM and site applicability still require an isolated AD pilot.

## 0.4.0-preview.75 — Explicit type selection, correct skip stages and matching campaign names (2026-11-09)

- New Campaign Wizard sessions start with **no** selected campaign type; Next stays disabled until Lock Screen, Screen Saver or Both is chosen.
- Images, Arrange and Schedule show "(skip)" **only** for Screen Saver, immediately on selection; switching to Lock Screen or Both removes all skip labels.
- A new untitled draft gets a type-specific suggested name; manually entered names are not overwritten. The New campaign dialog permits an untitled draft and lets the Wizard supply the name.
- New drafts remain type-gated even when images are imported through Advanced. Historical saved campaigns retain their inferred mode. This temporary state is never written into JSON or deployed client packages.
- Correct the Wizard landing and main editor step summary from six to seven steps, including Campaign Type.
- Windows x64/x86 automated builds and simulated DPI WinForms checks cover the selection and skip transitions. Actual 200% hardware DPI and AD user-policy application require separate testing.

## 0.4.0-preview.74 — Choose campaign type BEFORE images in seven-step Wizard (2026-11-09)
- Add a first-class Campaign Type wizard step immediately after AD Connection: Lock Screen, Screen Saver, or Both.
- Show built-in .scr selection, idle time (60–86400 seconds), and sign-in on resume directly on the Type step. Screen Saver-only skips JPEG selection, arrangement, and change schedule.
- Keep all image steps for Lock Screen and Both. The Back button from a Screen Saver-only Save step returns to Type. Skipped steps are visibly identified.
- Persist explicit CampaignMode and Screen Saver options in project JSON; infer modes for older preview.72 group assignments, and preserve legacy image-only projects as Lock Screen.
- Support validation/AD-GPO menu for Screen Saver-only projects with no JPEGs; require a scoped user OU and security group before deployment; never create a computer GPO when Screen Saver targets are not configured.
- Add UI smoke for all seven steps at 100–200% and direct Type-to-Save with zero images, plus self-test JSON roundtrip and mode/group conflicts.
- Includes Screen Saver Registry.pol strict binary integrity fix merged separately in PR #14. First verify real user GPO settings in an isolated AD test OU.

## 0.4.0-preview.72 — Separate Lock Screen / Screen Saver / Both assignments per AD group (2026-11-09)
- New per-campaign assignment matrix: up to 20 rows, each mode LockScreen, ScreenSaver or Both, independent computer OU/security group and user OU/security group. Existing projects preserve the legacy Lock Screen flow.
- New native C# User Configuration GPO creates a Windows built-in Screen Saver configuration: selected standard .scr, idle timeout 60-86400 seconds, optional password on resume. No automatic loopback or domain-wide link.
- Both mode creates separate computer/user GPOs with distinct security filtering, individually verified read-only previews, explicit batch approval, independent receipts and clear partial-success warnings.
- ScreenSaver-only campaigns need no JPEGs; LockScreen and Both retain validated JPEG schedules. The image slideshow is not yet a managed .scr and is not claimed to be.
- Added six-scale WinForms smoke for the assignment wizard, MS-GPREG Registry.pol encoding tests, user/computer scope checks, project JSON roundtrip and rejection of duplicate assignments.
- Batch native EXE agent deployment remains blocked; PS1 is the production Lock Screen client. Real AD user/computer pilot tests are still required.

## 0.4.0-preview.71 — DPI/Client Health/SMB usability + Bug Reports review (2026-11-09)
- Fix clipped Campaign Wizard Schedule/Client Health and bottom helper text by allocating additional scrollable content and status space.
- Show actual current UTC offset, PC UTC offset, difference in hours/minutes and DST state in both schedule editors. Preserve Windows time-zone IDs so future DST changes do not become fixed-offset schedules.
- Make long SMB share roots and Campaign Wizard Client Health details horizontally scrollable. Add Browse SMB and Copy path actions to Client Health and Feedback Settings; GPO Operations exposes full OU DN in a scrollable viewer.
- Increase readable header/footer and DataGridView row heights throughout Client Health, Device Repair and GPO Operations. Fit single-line text rows based on rendered fonts.
- Expand UI scale preferences and offline multi-window WinForms checks to 175% and 200% (previously silently clamped to 150%), alongside 100%, 125%, 150% and 187.5% font simulation.
- Reviewed published GitHub Bug Report #11. Missing GPO selection now disables lifecycle Preview and displays an instructional status without generating an error report or touching AD/GPO.
- Keep GitHub diagnostic Issues review opt-in/public-safe, read-only, and scheduled via existing workflow.

## 0.4.0-preview.70 — Opt-in Agent Management auto-refresh and fair RPC fleet polling (2026-11-09)
- Fix high-DPI clipping reported in three screens: make the Software updates subtitle/both header rows scale with the outer container, reserve the full Continue text in Campaign Wizard, and allocate scrollable/DPI-aware height to the Advanced connection options checkbox and its expanded DC controls.
- Extend multi-font WinForms layout smoke to assert the exact affected labels, full Continue caption, and expanded Advanced connection controls.
- Auto-refresh every 60 seconds now refreshes both selected-campaign SMB agent reports and separate RPC TCP/135 reachability hints while Agent Management is visible; the option is off by default and never performs remote writes.
- Reachable-only is a display filter, not a sampling gate: previously unreachable or untested AD-verified computers remain eligible for the next probe, subject to the selected name/AD activity filters.
- Rotate the bounded 300-host read-only reachability sample by oldest check time, so repeated scans progressively cover larger verified OUs instead of checking the same first 300 forever.
- Protect the refresh timer against overlapping SMB/RPC requests and GUI-close races. Unknown or inaccessible feedback remains UNKNOWN rather than zero or offline.
- Preserve separate AD activity, RPC, client-reported version/type/health and remote task-submission outcomes. Native EXE production rollout remains blocked pending live AD and Windows 10/11 tests.
- Add offline contract checks and a specific operator lab acceptance checklist.

## 0.4.0-preview.69 — Visible version in all main windows and explicit agent method telemetry (2026-11-09)
- Show a live application version label in the main editor, Campaign Wizard, AD Connection, Deployment and Agent Management headers; the AD account picker and Windows window titles also show the same packaged version. Retain the full product title and test heading readability at multiple simulated DPI/font scales.
- Both PS1 and native EXE agents now include explicit clientType in the per-PC campaign JSON: PowerShell or Executable. Client Health shows version and client type in the grid/CSV; Agent Management compares the client-reported type against the verified managed GPO method and flags a reported mismatch, without authorizing remote actions based on untrusted telemetry.
- Schema-v1 reports from older agents without clientType remain supported and are shown as Unknown/legacy, not assumed PS1. Unsupported clientType values are rejected. Renamed stale report condition to avoid falsely asserting that the computer is offline.
- Updated offline tests for dynamic release version labels, PS1/EXE telemetry, legacy JSON, malicious fields and GPO method comparison. No live DC, SMB or endpoint acceptance test has been completed.
- Native EXE deploy remains isolated-lab-only and blocked from production.

## 0.4.0-preview.68 — Dual PS1 and native EXE agent methods (EXE lab-only, 2026-11-09)
- Added an explicit **Client / GPO method** dropdown on the OU/group deployment Wizard step: PS1 with PowerShell Startup Scripts, or a pinned native EXE with classic Startup Scripts. Choice is saved in the campaign manifest, invalidates Preview, and is verified again before any write.
- Native EXE agent supports headless SYSTEM Install / Apply / Repair / Cleanup / Status / Report, deterministic per-computer image ordering, source/image SHA-256, task scheduling, Windows 64-bit policy registry view, feedback schema v1 and protected cleanup/restore. Production deployment remains blocked until live Windows 10/11 + DC tests; opt-in --native-exe-lab requires isolated OU and a flat security group of no more than 5 computers, explicit confirmation and revalidation.
- Native remote Push / Repair / Report / Cleanup dispatches the correct pinned PS1 or EXE client by the managed manifest method. No generic remote EXE command or silent conversion of existing managed GPOs.
- Brought the old laboratory branch forward without replacing the current shared AD sign-in Wizard, UI/DPI fixes, Agent Management, SMB reporting, AD inactivity filters or preview.67 functionality.
- Added x86/x64 CI tests for strict native CLI modes, EXE Status and SHA-256 tampering, dual GPO method contracts, native image mapping and actual painted image preview pixels.

## 0.4.0-preview.67 — AD OU activity filters and trustworthy Client Health counts (2026-11-09)
- Agent Management distinguishes three populations: all AD computer objects in selected OU/sub-OUs, verified OU + campaign security-group targets, and those targets with a valid same-campaign SMB report. Reports are non-cryptographic hints, not proof of presence or permissions.
- Read-only native LDAP Inventory queries replicated lastLogonTimestamp and userAccountControl; by default show computers active within the last 90 days and not disabled, with All / 30 / 60 / 90 / 180 / 365 / older-than-90 / never / disabled filters. No AD write, move or delete; Run Now still independently rechecks exact OU+group scope without using age as authorization.
- Present separate readable OU/group/visible/disabled/inactive counters and SMB report/recent/missing/invalid counters. SMB inaccessible is UNKNOWN, never reported as zero. Client rows also include agent-reported stage/version/timestamp and the latest per-host Task Scheduler submission result.
- Campaign Wizard Schedule now includes Client Health / Feedback settings and clearly shows the SMB UNC root and reporting interval; company defaults continue to work.
- Test validation for AD FILETIME, disabled bit, missing/forged/stale SMB evidence, and x86/x64 GUI smoke. No live writable DC, real Windows endpoints or authenticated agent delivery is proven by GitHub CI. The EXE endpoint agent remains gated in the separate lab-only draft PR.

## 0.4.0-preview.66 — Agent Management and live RPC reachability (2026-11-09)
- Consolidate existing secured Push / Repair / Report / Cleanup actions in the renamed Agent Management / Live tab, with AD-scoped computer list and search.
- Add bounded asynchronous TCP/135 reachability checks for verified OU + computer-group hosts, auto-run on opening the tab, optional 60-second refresh, and reachable-only filter / select up to 25.
- Show checked time and explicit Unknown vs RPC-reachable status. Endpoint mapper reachability does not prove Task Scheduler access, an agent heartbeat, or actual lock-screen image; use Report and Client Health for verification.
- Expose Agent Management / GPO Operations from the final Deployment Wizard page rather than Advanced mode only; add direct AD / GPO main-menu shortcut for company profiles with explicit OU/group defaults. Preserve Preview, full GPO GUID confirmation and fresh native OU/group revalidation before any remote task.
- PS1 remains the production agent. The EXE implementation stays gated in the separate laboratory PR; arbitrary remote EXE execution is deliberately unsupported.
- No live AD or real client RPC was tested in hosted CI.

## 0.4.0-preview.65 — Shared two-step AD user search and sign-in Wizard (2026-11-09)
- The startup AD Connection Wizard and the AD / GPO Deployment window now open the same two-step alternate-account Wizard: (1) live typeahead AD search by display name, username, account name, email or UPN, with a result list and exact-DN tooltips; (2) password entry after choosing the account, with Back to revise the choice.
- Native C# LDAP lookup starts after the first character (debounced, capped at 75 matches, signed/sealed Windows account binding, no PowerShell). Stale search replies cannot replace later results. An explicitly chosen manual login remains available when Windows-domain read access is unavailable.
- The selected account's password does not carry over when the user switches identities. Canceling a new alternate account selection restores the previous Windows account mode; no AD write occurs during lookup.
- Preserve the startup Wizard's optional Credential Manager-backed remember/save/forget controls. Deployment credentials remain session-only; remove the extra inline password editor and older one-off Find User popup.
- Add offline typeahead/security contracts and WinForms search/password page smoke across multiple font/DPI profiles; require GitHub Actions x86/x64 passes before shipping.

## 0.4.0-preview.64 — Deployment Wizard rights and readable targets (2026-11-09)
- Make the read-only GPO/OU rights check advisory: unknown or unconfirmed ACL hints do not interrupt a safe Preview with a blocking confirmation. GPO creation and SYSVOL writes still require a separately confirmed deployment and fresh native checks.
- Keep AD connection status distinct from color-coded permission hints (potential allow, possible deny, incomplete group token, no matching allow, unavailable). Explicitly mark SYSVOL and GPO COM write rights untested.
- Give connection, OU/group selection and Preview distinct full-height/full-width Wizard stages. Hide redundant connection fields after connection; use concise list names, searchable full DNs and hover tooltips for exact OU/group identities.
- Add regression checks for advisory preflight and offline Windows x86/x64 multi-scale Deployment Wizard layout.
- Existing PS1 client deployment is unchanged. A separate native EXE client with its own GPO delivery mode is not part of this preview; do not claim client EXE parity until verified. No live domain write test was performed.

## 0.4.0-preview.31 — AD wizard, DNS auto-discovery, image import and icons (2026-11-08)
- Public GitHub Release updater uses the pinned /releases?per_page=100 endpoint for both channels. Stable-only with no stable release returns a normal empty result instead of an HTTP 404.
- AD-first start is a three-step BitKeyBridge-style connection wizard with left-side progress, read-only LDAP inventory and explicit final review. Windows DNS SRV automatically discovers DC FQDNs for the detected domain. Long DC names remain visible in a wide dropdown and tooltip.
- Campaign editor offers Add Files... (multi-select JPG/PNG/BMP) and Add Folder... (top-level supported files), with duplicate/file validity checks and GPO deployment remaining separate.
- Fix header clipping at higher DPI. Embed a custom EXE/application icon and consistently assign visual icons to action buttons across all WinForms dialogs.
- Keep strict Windows x86/x64 CI gates, privacy/source checks, updater SHA-256 validation and manual install policy. Production DC/GPO and physical Win11 acceptance remain pending.

## 0.4.0-preview.30 — public updater and diagnostics acceptance (2026-11-08)
- Fix the original Windows GUI CI false-positive: self-tests now run with Start-Process -Wait -PassThru, check the actual WinExe process exit code, and verify an explicit non-zero exit probe. The older green pipeline incorrectly hid a failed updater test.
- Repair the updater synthetic future-release fixture and test both public anonymous and private token-scoped metadata/downloads, GitHub CDN redirects, SHA-256, ZIP safety, PE architecture and unsigned Authenticode rejection. No unattended installation.
- Public GitHub Issues instructions require explicit privacy review and manual submission; ensure local diagnostic logs redact synthetic DC, domain and IP information.
- AD-first snapshots are invalidated on new connection attempts, selected DC, OU/security group/company changes and expiry. Deployment independently reconnects and validates scope.
- Build x86/x64 and full/individual tests before an automated GitHub prerelease upload. Publication requires both tested ZIP architectures, single-EXE updater ZIPs and a SHA-256 manifest. Real DC and Windows 11 client deployment remains untested.

## 0.4.0-preview.28 — AD-first startup, BitKeyBridge UI and actionable diagnostics (development)
- Begin with **Step 1 Connect to DC & Read AD** (one signed + sealed read-only LDAP directory snapshot including DCs, OUs, security groups and GPO names); explicit offline mode does not authorize deployment. Revalidate DC and OU/groups again before GPO preview.
- Apply BitKeyBridge-inspired navy/blue/green UI tokens, high-contrast fallback, readable connection status and DPI layout smoke. Never store AD passwords.
- Empty campaign/image selection is user-input validation rather than a crash/diagnostic Issue; new manual GitHub Issue flow explains private-repository 404 and pending local delivery.
- Synthetic C# tests, PowerShell 5.1 and offline CI only; not real domain/Windows 11 acceptance. Stable and published preview.27 remain untouched.

## 0.4.0-preview.27 — RSoP Registry CSE + Image Integrity (2026-11-08)
- Add read-only **Verify RSoP + image** to the Existing GPO conflict UI with fresh selected-DC validation of managed GPO and AD computer OU/group scope. Query cached computer **RSOP_RegistryPolicySetting** by WMI/DCOM with bounded per-setting precedence/GPO GUID and explicit Unknown/Unavailable states; no gpupdate, WinRM, RSAT or remote scripts.
- Check only campaign-owned C: ProgramData JPEGs through read-only C$ SMB, reject arbitrary/reparse paths, compare SHA-256 to selected DC campaign.json and report hash match/mismatch separately from the physically displayed image (always unproven).
- Show CSE source and JPEG hash evidence alongside estimated winning policy and observed HKLM with private CSV, tests for malformed/forged/duplicate evidence, GUI/DPI smoke and Windows PowerShell 5.1 safety assertions. Manager, agent and updater **0.4.0-preview.27**; stable untouched, no real DC/Win11 acceptance.

## 0.4.0-preview.26 — Actual Endpoint Observation (2026-11-08)
- Add opt-in **Verify endpoint HKLM** to read-only Effective GPO analysis; revalidate managed GPO, computer, OU and security-group scope on chosen DC before reading a single Windows machine's native 64-bit HKLM through Remote Registry. No gpupdate, remote scripts, service activation or GPO changes.
- Capture LockScreenImage / NoLockScreen / Spotlight and PersonalizationCSP values with explicit Present/Absent/Unreadable/Unsupported/Unavailable and Windows 11 OS hint. Compare with static winner estimate as **value match/difference only**, never attribute the winning GPO or prove physical display. Historical gpresult remains unsigned GPO-level evidence.
- Add strict bounded response parser, C# mismatch/match/MDM/offline/partial/non-Win11 fixture tests, Windows PowerShell 5.1 non-mutating contracts, English WinForms/DPI controls and private CSV columns. Manager/embedded agent/updater to **0.4.0-preview.26**. Stable unchanged; real domain/Win11 acceptance still deferred.

## 0.4.0-preview.25 — Winning Policy Analysis (2026-11-08)
- Add per-setting **Winning policy analysis** to read-only Effective GPO candidates: candidate static priorities, conflicting values, GPMC Link Order 1, link Enforced, source/registry details, user-searchable English WinForms/DPI report and manually approved local CSV.
- Correct the effective scanner to traverse the **real selected AD computer container** under the search OU; expose scope depth and per-link order. Keep Site and client RSoP uncertainties explicit and no claim that an applied GPO verifies an individual registry setting.
- Synthetic C# model/WinForms UI tests and offline Windows PowerShell 5.1 GPMC link-order coverage in hosted x86/x64 CI. App, bundled agent and updater advanced together to **0.4.0-preview.25**. Stable remains unchanged; no real AD/DC/Windows 11 test.

## 0.4.0-preview.24 — local RSoP XML evidence import (2026-11-08)
- Add **Import gpresult XML** and **Clear imported RSoP** to the existing Effective GPO candidate dialog; compare selected-DC candidates with a manually supplied computer-scope `gpresult /scope computer /x` report by GPO GUID only. Display client-reported applied/filtered, insufficient flags, not listed (unknown), historical timestamp and source SHA-256 independently from AD eligibility. Show **RSoP-only GPOs** absent from the AD candidate inventory.
- Harden XML import: local regular files ≤12 MiB, reject UNC/device/junction paths, prohibit DTD and external entities, check expected computer/domain, logged data, unambiguous timestamps, duplicate/contradictory identifiers, and cap imported GPOs. Unsigned snapshots are never trusted as proof of currently winning lock-screen setting.
- Add manually confirmed private CSV columns, standalone C# synthetic XML SelfTests, and WinForms/DPI regression checks. No changes to client scripts beyond version alignment. Manager/agent/updater **0.4.0-preview.24**; stable unaffected; no live DC/Win11 acceptance.

## 0.4.0-preview.23 — Effective GPO conflict candidates (2026-11-07)
- Add **Analyze effective (PC)** beside the unchanged **Scan OU + GPO**, binding the target to a real selected-DC AD computer in the chosen OU AND computer security group, and to the selected managed GPO's manifest/startup identity.
- Inspect OU ancestry, Enforced/disabled links, Block Inheritance, machine configuration, computer tokenGroups and GPO DACL Read/Apply ACL hints, WMI filter presence and possible site-linked GPOs. Mark unknown site, WMI result, security access and script/GPP effects as requiring endpoint RSoP; do not claim effective policy without evidence.
- Reuse the safe Registry.pol / GPP Registry.xml scanner for LockScreenImage, NoChangingLockScreen, NoLockScreen and Spotlight candidate settings. Provide English DPI-aware policy filter/search, details and explicitly approved local private CSV. No policy mutation or remote task execution.
- Add Windows PowerShell 5.1 offline helper/read-only tests, C# scope and uncertainty model SelfTests, and WinForms UI smoke. Increase application/agent/updater to **0.4.0-preview.23**; stable unchanged. Real AD and Windows 11 integration test deferred.

## 0.4.0-preview.22 — read-only DC consistency audit (2026-11-07)
- Add **GPO Operations → DC Consistency** tab with selected managed-GPO context, signed LDAP discovery and explicit 1–12 DC selection (selected DC is baseline), DPI-safe English UI and manually confirmed private CSV export.
- Compare RootDSE `currentTime` against local UTC sample midpoint and between named DCs; flag offset over 300 seconds or cross-DC differences over 120 seconds. Verify DNS/domain identity; do not assume local computer time is authoritative.
- Verify selected campaign's manifest/agent/images SHA-256, exact numbered files, startup script target, `gpt.ini`, `psscripts.ini` and LDAP `versionNumber` on each **named** DC. Show mismatch, clock drift, invalid/unknown DC, and partial (>12 DC) coverage distinctly; never claim DFSR convergence from a file hash scan.
- Add pure C# cross-DC tests, Windows PowerShell 5.1 offline helper/command checks, WinForms DPI smoke and bundled resource. Version app, agent and updater **0.4.0-preview.22**; stable unchanged. No real AD or Windows 11 integration run.

## 0.4.0-preview.21 — safe post-cleanup verification (2026-11-07)
- Add an agent-side **post-cleanup receipt** produced only after task/file removal and local registry reinspection. Keep existing early Completed Client Health as a progress hint, not a deletion guarantee; old installations without the receipt remain unverified.
- In Closure audit, offer manual **Verify selected (read-only)** for 1–25 computers, restricted to the AD-verified selected OU/security group and managed campaign/GPO. Inspect remote SMB files/owner, Task Scheduler tasks and HKLM original-value restoration via read-only Remote Registry. Never schedule tasks or run remote command scripts for verification.
- Persist Last observed states locally per exact domain/DC/OU/group/campaign/GPO, preserving offline/Unknown machines across reopen and partial manual rescans. Reject malformed, out-of-scope, partial, stale and internally inconsistent verification claims; no automatic retries or GitHub uploads.
- Add safe private CSV fields, WinForms DPI compatibility and x86/x64 isolated agent/queue evidence tests. Live AD and Windows 11 testing deferred; stable untouched. App/embedded agent/updater version **0.4.0-preview.21**.

## 0.4.0-preview.20 — conservative campaign closure review (2026-11-07)
- Add **Closure audit** to the verified Device & Repair Dashboard, with per-computer conservative classification of agent-reported completion, unresolved User/gMSA Repair, still-active/pending devices, stale/errors and missing feedback. Never interpret a missing report as success or failure.
- Query the selected DC's **managed GPO link list** read-only when the operator opens the window; show direct OU link enabled/disabled/absent, computer settings state and count of other links separately from agent reports. If the DC read fails, mark policy evidence **Unknown**; no RSoP or AD replication guarantee.
- Add searchable DPI-aware English UI, saved window geometry and manually confirmed local CSV export, with spreadsheet-formula safeguards. Add isolated pure-model and GPO link tests plus WinForms DPI smoke tests. No DC/GPO/SYSTEM changes in CI.
- Keep the interface safe: agent Completed is emitted before final cleanup; never claim a clean fleet on that basis. A verified post-cleanup endpoint read is future work. Application/agent/updater increment to **0.4.0-preview.20**; stable unchanged.

## 0.4.0-preview.19 — persistent User/gMSA Repair history (2026-11-07)
- Record transitions in bounded, per-month JSONL alongside both credential-free User and protected gMSA queues. Preserve approvals, RPC starts, interrupted/uncertain results, review-needed states, fresh Client Health confirmation, cancellations and superseded approvals after active tracking entries are removed. Ordinary feedback polling does not count as another remote RPC.
- Add read-only **Repair history** window to Device & Repair Dashboard: verified AD scope, most recent events, source/device/action filters, a DPI-aware layout, saved window placement and opt-in CSV export. Do not add RPC privileges or automatic GitHub reporting.
- Detect truncated/malformed journals; write under existing queue locks after atomic queue changes. Explicitly document possible crash-related gaps and protected local operational data. Add Windows x86/x64 queue and history regression tests; no real AD/GPO changes in CI.
- Increment application, embedded agent and updater to **0.4.0-preview.19**; stable unchanged.

## Runner-free offline Live AD + Windows 11 test kit (preview.18 tooling update, 2026-11-07)
- Add a separate GitHub-hosted workflow that packages a self-contained **offline lab test ZIP** from trusted repository scripts; Windows PowerShell 5.1 parsing and static safety checks run without connecting to any domain.
- Add a manual workstation launcher, example isolated-OU JSON configuration, Preflight and explicitly approved Full commands, and **local-only** domain/Windows 11 test reports. The kit reuses the existing live AD probe and GPO engines without installing a GitHub self-hosted runner.
- Ordinary CI and this package do not perform an actual DC/GPO test; the operator must run the kit on a delegated domain-joined management workstation with disposable Windows 11 lab PCs. No application version change or stable release.

## 0.4.0-preview.18 — GitHub-hosted Windows client agent integration (2026-11-07)
- Run real **production PowerShell agent functions** in ordinary x86/x64 GitHub Windows CI, using isolated per-run HKCU policy keys and disposable files. Verify first/second image changes, immutable source SHA-256 repair, registry backup and owner integrity, external GPO conflict refusal, tampered backup rejection, completed-campaign restoration and idempotent cleanup.
- **Registry restoration fix:** avoid calling `New-Item -Force` on an existing Personalization registry key, which can delete unrelated policy values. Create only missing keys; preserve original zero-valued DWORDs and third-party values. The hosted Windows suite checks these cases before and after Apply/Cleanup.
- Never write production HKLM, AD, GPO or SYSVOL in hosted CI. Scheduled task service, startup permissions and reporting transport remain explicitly mocked in these tests. A green hosted build does not prove physical Windows 11/DC integration.
- Maintain the separately protected manual Live AD/Win11 Lab workflow for domain-connected runner testing. Version app, embedded agent and updater to **0.4.0-preview.18**. No stable release.

## 0.4.0-preview.17 — gated live DC + Windows 11 integration workflow (2026-11-07)
- Add optional **Live AD + Windows 11 Lab** GitHub Actions workflow, exclusively manual and gated on a private self-hosted Windows lab controller, exact approval phrase and the protected `ad-lab` environment. No normal pull request or scheduled job can access a live DC.
- Preflight confirms the writable DC and its UTC time, an isolated `LSCM-CI` OU, a matching test-only computer security group, Windows 11 Enterprise/Education target PCs, domain time, existing lockscreen conflicts, and absence of prior campaign state.
- Full lab mode uses the existing GPO Deploy and Lifecycle engines: creates a unique two-image campaign and GPO, reboots only designated test PCs, verifies real Startup GPO, gpresult, SYSTEM agent and scheduled task, SHA-256, the registry image value, a timed second image, client cleanup and exact GPO/SYSVOL cleanup. Partial failure is fail-closed with explicit report and preserve-for-review safeguards.
- Extend ordinary Windows x64/x86 CI with offline safety contracts and PowerShell 5.1 syntax checks of the lab scripts. These build checks are NOT a real domain test. Keep application, embedded agent and updater aligned at preview.17. Stable remains unchanged.

## 0.4.0-preview.16 — Smart SYSVOL Storage and verified DC time (2026-11-07)
- Introduce **Smart SYSVOL Storage** in GPO Operations: bounded, read-only audit of existing campaign source folders; SHA-256 comparison of normalized JPEGs; verified duplicate counts and a separate comparison for the currently selected campaign before GPO deployment. Keep every campaign's files separate; do not overwrite existing IDs, hardlink images, or silently rewrite manifests.
- Add an administrator-only **Preview cleanup** and explicitly typed campaign-ID confirmation, never an unattended cleanup job. Eligibility requires end time plus 30 days, valid source hashes and exact layout, no GPO references, writable selected DC, a complete GPO audit and a five-minute content-bound plan. Any uncertainty blocks destructive operations; live AD and offline client acceptance remain necessary.
- Use the selected DC's LDAP RootDSE `currentTime` as the source of UTC for deployment expiration, GPO Reuse and SYSVOL cleanup; display DC UTC time and workstation/DC offset. Block writes when clock skew exceeds five minutes; keep client-side Windows Time scheduling and established time-zone/DST validation.
- Expand embedded PowerShell 5.1 offline security regression and DPI smoke coverage for the new tab. Version application, agent and updater to **0.4.0-preview.16**. No stable release.

## 0.4.0-preview.15 — unique schedule-slot matching and computer primary group safety (2026-11-07)
- Suppress automatic image mapping when a date-only or weekday-only filename matches multiple scheduled changes (e.g. an interval changing images several times on Monday, or a weekday repeated in a multi-week campaign). Prefer a uniquely numbered matching filename rather than letting an ambiguous high-scoring filename obscure it. The operator may still resolve unmatched slots manually in the existing review dialog.
- In Computer & Group Assignment, detect an AD computer's primary group with its verified primaryGroupID and the selected security group's SID RID. Show **Primary** in read-only search, block Add/Remove in preview/apply, and verify primary group data on the chosen DC. Other approval, writable DC, OU, GUID, uSNChanged and typed SAM safeguards remain active.
- Extend deterministic C# self-tests for interval/day/date and multi-week slot collisions, offline Windows PowerShell 5.1 tests for primary group protection and UI explanation. No real AD write is performed by CI; a controlled writable-DC pilot is still mandatory.
- Increment application, UniversalUpdater fallback, and embedded agent to **0.4.0-preview.15**; no stable release.

## 0.4.0-preview.14 — safer group assignment and image-name inference (2026-11-07)
- Normalize Active Directory group `uSNChanged` whether ADSI exposes a 64-bit integer or a COM high/low IADsLargeInteger; fail closed if it cannot be read. This guards the existing expiring Preview/Apply token against unseen group changes.
- Reject modifications to built-in/default security groups by verifying actual binary SID RID, not only display name or `adminCount`. Preserve other OU, GUID, writable DC, direct membership and typed-computer confirmation checks; treat missing RootDSE `isRODC` as unknown/non-writable.
- Immediately before changing a group member, re-read and verify the computer's distinguished name, computer GUID and SAM; refuse if it was moved or replaced after preview.
- Recognize malformed or multiple calendar dates in image filenames as conflicting hints (manual review). Strip date token digits when extracting ordinal hints, but verify independent ordinal clues even when a valid date is present. Do not auto-assign a file with contradictory date/weekday/ordinal details.
- Add dedicated Windows PowerShell 5.1 offline USN/SID regression tests and C# filename-inference tests. Windows x86 and x64 CI remain release gates. No GPO or AD modifications run in CI; real writable-DC pilot remains necessary.
- Increase application, UniversalUpdater fallback, and agent version to **0.4.0-preview.14**; no stable release.

## 0.4.0-preview.13 — interval changes, folder image inference, Computer & Group Assignment (2026-11-07)
- Increment application and agent to \`0.4.0-preview.13\` (portable ZIP names derive from csproj). Add fifth **Every N hours/minutes** schedule mode with separate hours/minutes fields, 00:01–168:00 permitted interval, exact elapsed-minute timestamps and DST validation. The original Daily/Workdays/CustomDays/SpecificDates projects remain backward compatible.
- Support **schedule-first workflow:** choose company, campaign start, calendar days or minute interval, then **Schedule → Import image folder...**. Scan up to 30 JPG/JPEG/PNG/BMP files in the selected folder. Recognize weekday names (English, Russian, Hebrew), 01–30 numeric ordinals, and unambiguous ISO / dd.MM.yyyy dates; match only strong, unique filename clues to precomputed schedule slots. Display a complete preview with scheduled date/time, matched image and reason. Weak/ambiguous names require manual selection and explicit review confirmation; duplicates and missing file assignments are rejected. No GPO, AD, file mutation or client call during import.
- Add **Computer & Group Assignment** to GPO Operations: search the verified company OU, display each computer's direct/nested security-group membership, and preview a single Add/Remove operation on the verified GPO filtering group. A writable DC, original OU/group scope, current computer/group GUIDs and group uSNChanged, an expiring 5-minute preview digest and typed exact computer SAM (including \`$\`) are checked before the AD change. Protect critical/privileged groups; do not auto-add arbitrary PCs or remove nested memberships. Use embedded Windows PowerShell 5.1 with signed/sealed LDAP; no RSAT, external PS1 install, GPO modification or remote client RPC.
- Add offline C# image-name inference tests and interval/DST boundary tests, WinForms multi-scale smoke tests for new screens, and Windows PowerShell 5.1 parsing/OU-scope/filter-injection/plan-hash checks for the group assignment engine. CI builds on x86/x64 required before merge.
- Real domain group-membership acceptance, device token refresh, graphical 200% DPI and a private GitHub Release test remain required before any stable release.

## 0.4.0-preview.12 — flexible image schedule and stable per-device image order (2026-11-07)
- Add **Campaign Schedule** editor in the main campaign window (English WinForms, native DPI-aware) with **Every day**, **Work days (Mon–Fri)**, **Custom weekdays**, and **Specific dates** modes. Provide a calendar with marked change dates, exact date/time per image in Specific dates, and **Repeat selected image** to use the same JPG on multiple dates.
- Separate immutable **scheduled times** from optional **Randomize images per device**. Each Windows client computes a repeatable SHA-256 permutation from campaign ID, computer name, and numbered image filenames. The mapping survives service restarts/reboots, covers each normalized image exactly once, and is used consistently by agent Apply/Client Health. An optional sample computer name in the schedule editor previews that machine's deterministic order without connecting to AD.
- Keep manifest schema version 1: export precomputed, strictly increasing \`atLocal\` timestamps and optional Boolean \`randomizePerDevice\`. Old campaign projects/manifests default to Daily + fixed image order. GPO manifest validator rejects malformed new Boolean. Preserve all image hashes and client recovery/conflict checks.
- Validate schedule date order, exact count, weekdays, 30-image cap, two-year horizon, DST invalid/ambiguous local times, and end cleanup (last image + 1–72 hours). In workday/weekdays mode an image remains until the next scheduled change, including weekends.
- Integrate the schedule editor with Add/Remove/Replace/Reorder and ensure a fresh AD/GPO preview retains advanced schedule properties. Extend C# self-tests (including real JPEG export, duplicates, irregular dates, DST, deterministic mapping) and run extracted production PowerShell agent permutation/slot functions under Windows PowerShell 5.1 on both x86/x64 CI. Include the editor in simulated DPI GUI smoke tests.
- Increment application version to **0.4.0-preview.12**; use csproj-driven ZIP naming. No automatic production GPO modifications, credential sharing, arbitrary RPC or stable release until domain pilot.

## 0.4.0-preview.11 — Company Profiles and company-scoped campaigns (2026-11-07)
- Add dedicated **Companies** workspace selector with search, create, edit, duplicate, explicit unassigned workspace and deletion safeguards. Add **New company profile** settings dialog with company name, AD DNS domain, optional preferred DC, computer OU DN, security group DN, feedback SMB share, enable-feedback flag and Windows time zone.
- Store up to 40 profiles per Windows user in an atomic, size-checked, no-reparse-point JSON file. Passwords, gMSA identities, tokens and AD credentials are NEVER stored in company profiles; company selection itself performs no AD, RPC, GPO or Task Scheduler writes.
- Integrate the active company into the main campaign editor with a visible company/domain banner and a **New campaign** action. Warn before discarding unsaved edits or closing; preserve the company association in project JSON. Legacy unassigned projects keep their original campaign ID format; new company projects use a stable, company-namespaced campaign ID to avoid collision across domains.
- Prepopulate the native AD/GPO wizard from the selected company only; never load/write its legacy global connection/username cache in company mode. After Connect, select preferred OU/group only if exact entries were returned by a fresh LDAP discovery; verify connected domain equals the company domain and continue to require Preview, conflict check and explicit deployment confirmation.
- Add offline regression tests for local profile persistence, duplicate names, invalid foreign DNs, SYSVOL share rejection, safe switching/deletion, missing/corrupted data, company-specific campaign IDs and legacy compatibility. Add Company Profiles and Company Editor to Windows x86/x64 DPI layout smoke checks.
- Increment application and ZIP version to **0.4.0-preview.11**. UniversalUpdater, UniversalDiagnostics, gMSA Repair authorizations and real AD/production readiness conditions remain unchanged.

## 0.4.0-preview.10 — CI diagnostics review and WinForms layout smoke (2026-11-07)
- Increment application version and portable EXE/ZIP artifacts to `0.4.0-preview.10` using the project version as the single source of truth.
- Run the read-only GitHub diagnostic Issues review on `main` when its own workflow or parser/tests change, in addition to its existing daily/on-Issue/manual triggers. This provides an initial verifiable job without posting Issues or collecting telemetry.
- Add a Windows x86/x64, in-process, noninteractive `--ui-smoke-test` after self-tests: instantiate Updater, gMSA Readiness, Diagnostic Trends and Diagnostics; check expected controls, layout, and off-screen window restoration under simulated 100%, 125%, 150% and 187.5% font-scale profiles. No network, AD, GPO, RPC, task installation or persisted user settings.
- Give native gMSA Setup action rows extra height and wrapping at enlarged font sizes.
- Keep UniversalDiagnostics and UniversalUpdater safeguards; no automatic release publishing or gMSA production claims. Actual multi-monitor high-DPI visual acceptance and live AD tests remain required.

## 0.4.0-preview.9 — private updater access, Authenticode and rollback inspection (2026-11-07)
- Increment application and single-file portable build version to 0.4.0-preview.9. Windows CI now reads the version directly from the project file when naming ZIP artifacts, preventing stale preview package labels.
- **UniversalUpdater:** optional masked GitHub read-only session token, memory-only in the Updates window. Metadata and private asset GET requests are restricted to the exact pinned GitHub API repository and numeric release asset ID; token is never stored, logged or included in the CDN redirect.
- Treat private GitHub HTTP 404 as an access/visibility error, not proof that no newer release exists. Preserve anonymous public release checks.
- Downloaded packages retain strict size, SHA-256, safe ZIP and PE architecture verification. Show native WinVerifyTrust Authenticode trust status as a separate security signal; a matching GitHub SHA alone is never a publisher signature and no unpinned unattended install is permitted.
- Add **Backup status** read-only inspection of a local .bak checkpoint. No in-process EXE swap, background downgrades, automatic rollback or gMSA task edits; protected worker must be stopped and updated by an authorized external procedure.
- Add isolated deterministic tests for private metadata + numeric asset URL, test CDN redirect without bearer, SHA-256 package integrity, ZIP and PE validation, invalid session token handling and no auto-install.
- UniversalDiagnostics, read-only gMSA Production Readiness and old Repair approvals remain unchanged. Real domain acceptance and live private GitHub release end-to-end authorization must be verified on an approved pilot host before stable.

## 0.4.0-preview.8 — unified HiDPI window placement (2026-11-07)
- Add shared per-user WinForms window placement for 11 administrative dialogs (GPO Operations, domain deployment, gMSA setup/readiness, history, diagnostics and trends, updater, client feedback, alerts and interface settings).
- Restore last window size/position at next opening, including maximized state where supported; clamp stale geometry to current monitor work areas after display unplugging, resolution or DPI changes.
- Store bounded JSON in LocalAppData, with no AD scope, client list, passwords or application logs. Ignore malformed, oversized or reparse-point geometry data without blocking the GUI.
- Apply the existing UiStyle fonts/DPI theme to the native gMSA Setup dialog; keep GPO Operations title version synchronized with assembly.
- Add offline WinForms geometry unit tests for multi-monitor, removed monitor, oversized HiDPI window, minimum size and invalid screens. Preserve manager 0.4.0-preview.8.

## 0.4.0-preview.8 — scheduled GitHub diagnostic Issue review (2026-11-07)
- Add read-only GitHub Actions workflow **Review published diagnostic Issues**, triggered daily, on Issue open/edit/reopen/label, or manually. Uses only the short-lived native GitHub Actions token with **issues: read** and **contents: read** permissions.
- Analyze at most 1000 existing Issues within the pinned private LockScreen repository. Accept only fully validated UniversalDiagnostics v1 JSON (plain or fenced), canonical ID/schema/project/fingerprint, safe category/error fields, SemVer, UTC and pending status; exclude ordinary Issues and pull requests.
- Produce a sanitized GitHub Actions job summary and JSON artifact with issue numbers, fingerprint groups and counts; never store raw Issue bodies or exception summaries in artifacts. No GitHub issues are created/edited, no code, GPO, client Repair or queues are changed.
- Run the offline malicious-input/duplicate/parser tests before every Windows build. Network review is a separate workflow; build never depends on external Issue availability.
- **No automatic client-to-GitHub diagnostic upload.** Operators must still review and publish selected sanitized reports; real GitHub privileges are granted to GitHub Actions only, not embedded in the EXE.

## 0.4.0-preview.8 — local UniversalDiagnostics trend analysis (2026-11-07)
- Add **Diagnostics → Error trends**: inspect and group up to 250 explicitly saved UniversalDiagnostics v1 records by the existing privacy-safe fingerprint; show occurrence count, error category, severity, first/last saved time and latest app version.
- Validate file size, non-reparse-point file, canonical project/schema, pending delivery, report ID matching filename, UTC timestamps, redacted fields, trusted fingerprint and SemVer before aggregating. Invalid/renamed/untrusted JSON is excluded and counted separately.
- Export an optional sanitized summary JSON that excludes raw exception messages, private file paths, per-report UUIDs, AD identity and hostnames. No automatic capture, GitHub upload or network requests.
- Add offline regression fixtures for correctly grouped captures and malformed/renamed data. Keep embedded UniversalDiagnostics v1 contract, UniversalUpdater and application 0.4.0-preview.8 unchanged.

## 0.4.0-preview.8 — Production Readiness and Device Repair Evidence (2026-11-07)
- Native **Production Readiness** in GPO Operations / Configure gMSA: 11 operator-initiated read-only checks, covering protected installation, matching AD scope, enabled worker, native Task Scheduler registration, local Netlogon gMSA account, fresh scoped heartbeat, fresh diagnostics executed under gMSA, administrator SMB access, protected queue readability, unconfirmed RPC and manual attention.
- Classify results as NOT READY / PILOT NEEDS REVIEW / READY FOR PILOT; even full PASS is never called "production-verified" or proof that a Lock Screen is displayed. Fail closed for disabled/mismatched/stale resources.
- Manually export minimal privacy-safe JSON, containing only standardized check descriptions, counts and opaque scope fingerprint. Never include raw gMSA SID, UNC share, AD domain/OU, hostnames, user secrets or queues. No GitHub upload.
- Expand Device Details with read-only per-device queue evidence: source, approval timestamp/14-day expiration, last attempted RPC, last submitted time, 20-attempt budget, next check, reason, and explicit warning for uncertain remote task. Label as latest tracked state, not fabricated historical attempts.
- Extend self-tests for classification, warning/blocked states, safe export, selected-device queue metadata. Preserve version 0.4.0-preview.8, original gMSA/queue authorization and UniversalUpdater/UniversalDiagnostics contracts.

## 0.4.0-preview.8 — Device & Repair Dashboard (2026-11-07)
- New **Device & Repair Dashboard** tab in GPO Operations: one per-computer view of the verified OU+group AD scope, Client Health SMB hints, both existing Repair queues and manually saved local history snapshots.
- Show expected image (agent-reported, never called visually verified), reported integrity, agent version, last agent check, unresolved Repair and archived per-device health transitions. Filter/search and optional 60-second refresh; no background RPC or hidden domain writes.
- **Repair selected** delegates to the existing read-only Preview + full GPO GUID authorization flow, with a fresh AD membership check. The dashboard cannot grant a new approval or invoke a second repair by itself.
- Close a duplicate-RPC gap: the foreground Repair path now inspects the protected gMSA queue even when the gMSA worker is disabled or its task removed. Queued records remain blocked while disabled; Attempting / AwaitingTask / AwaitingHealth always require investigation. Active Queued records can only be superseded in the existing protected administrator reservation transaction.
- Regression tests for AD identity ambiguity, campaign feedback isolation, archival history, distinct missing/healthy states, and disabled gMSA duplicate protection. Keep 0.4.0-preview.8, embedded UniversalUpdater and UniversalDiagnostics unchanged.
- No client screenshot or on-screen attestation exists: all client health is self-reported telemetry and final production AD/gMSA validation remains outstanding.

## 0.4.0-preview.8 — gMSA live queue health audit (2026-11-07)
- Add read-only per-computer queue health: expired approval, exhausted 20-attempt retry budget, delayed Queued, remote RPC in progress/unconfirmed, unknown task results, overdue Client Health and administrator review.
- Highlight attention items first; filter by state, monitoring and needs-review; show state and attention counts. Refresh the active gMSA tab every 30 seconds (optional).
- Read the protected queue and Task Scheduler off the WinForms UI thread to avoid freezing the manager on cross-process lock contention. Preserve selected row on refresh and stop the timer when the tab/form closes.
- Queue inspection never modifies approval timestamps, retries, Task Scheduler, GPO or remote RPC. No new PowerShell installer, unchanged shared UniversalUpdater/UniversalDiagnostics and 0.4.0-preview.8.
- Add offline tests for all health thresholds, no-retry policy and unchanged queue state. Live AD/gMSA/Client Health verification remains outstanding.

## 0.4.0-preview.8 — disabled gMSA queue read-only audit (2026-11-07)
- Restore visibility of the protected machine-wide Repair queue after the administrator clicks Disable. The gMSA tab now shows existing entries, states and per-state counts while Disabled.
- Introduce an explicitly privileged read-only inspector that validates the same SID/domain/OU/security-group approvals without setting Enabled=true, touching queue.json, extending approval windows, scheduling a task or executing RPC.
- Keep Cancel selected and routing of new Repair unavailable when gMSA is Disabled. Preserve existing queued approvals for normal resumption only after explicit native reinstallation.
- Add regressions for unchanged queue, authorization isolation, wrong AD scope and old Queued eligibility after re-enable. Keep the application version unchanged.

## 0.4.0-preview.8 — shared updater/diagnostics integration (2026-11-07)
- Embed 12 canonical .NET 10 UniversalUpdater 0.1.0-rc.1 sources, English/DPI-aware Updates window, pinned architecture/repository, stable/preview releases, and consented verified staging. No automatic EXE replacement or changes to gMSA.
- Package release-ready single-EXE update ZIPs in Windows x86/x64 CI. GitHub Releases must be published manually; the current private repository does not support anonymous checks.
- Refresh UniversalDiagnostics v1: current application version, MAC redaction, exact reviewed JSON persistence (same ID/time/pending state); reject tampered reports; no automatic GitHub submission.
- Add updater security tests, preview/capture integrity tests, gMSA Setup button recovery and scope-bound heartbeat. Retain version, queue and approval limits.

## 0.4.0-preview.8 — reboot-safe gMSA worker (2026-11-07)
- Register a delayed Windows boot trigger (3 minutes) alongside the existing daily 2-minute recurrence; requires Install / Repair in the native GUI to upgrade existing tasks.
- Recover persisted Attempting requests after reboot/crash to AwaitingTask, never automatically repeating a potentially already-registered RPC.
- Leave Queued approvals retryable within their original 14-day / 20-attempt limits, without granting new approval on reinstall.
- Add offline schedule verification and crash simulation tests, including idempotent recovery, no second RPC, and healthy client feedback completion.
- No script installer and no application version increment. Real gMSA/AD/reboot testing remains required.

## 0.4.0-preview.8 — gMSA diagnostic scope validation (2026-11-07)
- Bind read-only diagnostic reports to the installed gMSA SID, domain, OU, security group, SMB feedback share and executable path using SHA-256.
- Reject previous PASS reports after any scope change; display a clear rerun message when results are stale or older than one hour.
- Ignore legacy diagnostic reports with no scope fingerprint to prevent false-positive status.
- Add offline tests for cross-scope reports, changed SMB paths, changed group and gMSA SID.
- Preserve English GUI, native C# setup, and preview.8 version. Live AD integration remains required.

## 0.4.0-preview.8 — Native read-only gMSA test (2026-11-07)
- New "Run gMSA test" button in the English, DPI-aware native setup window.
- Additional no-trigger, on-demand task under the configured gMSA; no PowerShell installer or password collection.
- Verify gMSA process SID, executable/config ACLs, queue read, DNS and SYSVOL/feedback share read access.
- Write a protected local JSON diagnostic report and display results in Check.
- Disable/uninstall manages both Task Scheduler tasks; no remote Repair, no GPO mutation.
- Keep version unchanged; live gMSA and AD integration required before stable.

## 0.4.0-preview.8 — gMSA pre-RPC telemetry guard (2026-11-07)
- Read each due host's own SMB feedback again immediately before unattended RPC even beyond the first 20 polling entries.
- Skip remote repair when fresh matching client health already proves self-recovery.
- Hold malformed client telemetry for administrator review and pause execution on SMB access failures.
- Add offline C# scenarios for successful, missing, invalid and access-denied reports.
- No new password handling, PowerShell installer, or version increase. Live AD/gMSA testing remains pending.

## 0.4.0-preview.8 — gMSA GUI lifecycle and heartbeat hardening (2026-11-07)
- Retain account and feedback share when opening setup after Disable, without enabling the worker.
- Separate Task Scheduler registration from actual worker heartbeat, overdue status and operational errors in GUI and native diagnostics.
- Regression tests cover disabled, missing, recent, stale and failed worker heartbeats.
- Keep preview version unchanged; successful compilation does not replace real Windows domain integration tests.

# 0.4.0-preview.8 — Native gMSA setup in GUI (2026-11-06)
- Replaced external gMSA PowerShell installer with native WinForms setup wizard (check, install/repair, disable and remove task).
- Uses Windows Netlogon managed-service-account API, Task Scheduler COM and NTFS security descriptors directly from .NET; no gMSA .ps1 execution or saved password.
- Preserves original installed-at approval window on reinstall and blocks conflicting domain/OU/group identity changes.
- Removed gMSA installer script from artifact ZIP; existing gMSA queue and worker behavior unchanged.
- Live host-AD/gMSA runtime integration is still required, this remains a preview.

# 0.4.0-preview.8 — Unattended gMSA Repair (2026-11-06)
- Added separate secured machine-wide gMSA queue, no password and no dependency on interactive Windows logon.
- Added one-time installer/uninstaller for a protected EXE and gMSA Task Scheduler account with Password logon type (AD-managed credential).
- Dedicated gMSA worker validates its process account SID and protected settings; rechecks OU/group/GPO/campaign before each remote RPC.
- Added explicit gMSA 24/7 GUI routing for newly confirmed Repairs, protected queue listing and admin cancellation.
- Preserved the existing per-user background mode and crash-safe retry protections; new manual approvals suspend older unattended queues.
- Windows CI is source/offline validation only; real gMSA, AD ACLs and Task Scheduler integration remain untested.

# 0.4.0-preview.6 — Opt-in Background Deferred Repair (2026-11-06)
- Added a per-user Task Scheduler one-shot worker (two-minute cadence, only when the approving Windows user is logged on) independent of the GUI.
- Saved explicit background opt-in per new Repair request with the approving user's SID; no alternate credentials or stored passwords.
- Shared safe AD/OU/group/GPO preview validation for foreground/background RPC and a common client health confirmation protocol.
- Serialized queue updates across processes using a named Windows mutex; no extra RPC for ambiguous/crash-interrupted tasks.
- Added UI controls for install/uninstall and last background result. No RSAT, WinRM or service installation.
- Domain and Windows Task Scheduler integration testing remains pending; this is a PREVIEW.

# 0.4.0-preview.5 — Initial Repair tracking & safe retry (2026-11-06)
- Persist initial approved Repair outcomes: offline, task-success awaiting health, ambiguous task awaiting health; failed jobs are not auto-retried.
- Verify fresh self-healed client feedback before each offline RPC retry, avoiding unnecessary remote execution.
- Treat loss of RPC after remote task registration starts as ambiguous; do not classify as a safe offline retry.
- Bound and schedule SMB confirmation reads fairly; preserve AwaitingHealth after the last permitted RPC attempt.
- Added C# self-tests and static contracts for initial outcomes, stale feedback, self-healing and RPC ambiguity.
- This remains a PREVIEW; real AD and lock-screen visibility tests have not been completed.

# 0.4.0-preview.4 — Health-confirmed Deferred Repair (2026-11-06)
- Remote Task Scheduler exit code 0 now moves Deferred Repair to AwaitingHealth, not "completed".
- Poll per-host SMB client feedback, validating campaign, hostname, timestamp, JPG hashes, registry and local task.
- A fresh healthy report confirms and removes the queued repair; old/unhealthy/foreign reports cannot.
- Ambiguous remote tasks can also be confirmed from a fresh report without duplicate RPC execution.
- Two-hour feedback timeout becomes NeedsAttention; missing feedback never triggers unapproved RPC retries.
- Added regression tests for incorrect client, campaign, file/registry state and timestamp.
- Client SMB reports are diagnostic hints; true screen visibility still needs endpoint validation.
- Domain/SMB end-to-end testing is pending.

# 0.4.0-preview.3 — Deferred Repair (2026-11-06)
- Automatically queue temporarily offline computers only after approved Repair Selected.
- Retry while the authenticated manager window stays open; persist queue across restarts.
- Backoff, maximum attempts/approval age, status view, cancel and manual retry.
- Validate AD OU, nested computer-group membership, managed GPO and campaign on every attempt.
- No auto-repeat after uncertain task outcome, access denied or changed campaign.
- Dedicated PowerShell 5.1 offline classification and C# queue regression tests.
- This is preview code: real AD/Task Scheduler RPC transition testing is pending.

# 0.4.0-preview.2 — Client Management (2026-11-06)

- Added Repair Selected Computers (up to 25) from Client Health; rechecks OU/group membership and selected GPO CampaignID, previews target list and requires exact GPO GUID confirmation.
- Extended remote Task Scheduler RPC with Repair (and corrected Report mode validation); the signed/checked SYSVOL agent performs verified local restoration.
- Added opt-in local notifications on increasing error counts, configurable growth threshold/cooldown and optional stale-accounting. Never auto-remediates or uploads.
- Added local campaign event history (export, deployment, lifecycle, remote runs), manual health snapshots and final HTML/CSV reports with per-PC details.
- Protected HTML reports against injection and CSV reports against formula injection; history stored only in administrator LocalAppData.
- Added isolated C# unit checks and offline Python safety contracts; builds remain x86/x64 self-contained.
- Real domain deployment, RPC, SMB ACL and visual DPI tests remain pending.

# 0.4.0-preview.1 — GPO + Client Health / Live (2026-11-06)

- Combined existing GPO lifecycle, Lock Screen conflict scanner and targeted Push Now with Client Integrity/Self-Healing.
- Added an opt-in SMB client feedback protocol with periodic and event-driven JSON reports; no background listener on the admin machine.
- Added UI for dedicated SMB share, report interval, campaign folder creation, per-machine results and CSV export.
- Added AD OU+computer-group denominator, 15-second manager refresh and 45–180 minute stale classification.
- Restricted report fields to machine/campaign identifiers and health flags; no user/password/BitLocker/LAPS data.
- SYSVOL/NETLOGON and admin shares are rejected for reports. SMB/NTFS ACLs are managed separately.
- Added C# self-tests for telemetry classification and Python contract tests.
- Windows/AD end-to-end validation is still pending.

# 0.3.0-preview.1 (2026-11-06)
- Added safe GPO lifecycle: Disable, Enable, Unlink, Reuse and Delete (preview + typed GUID).
- Added read-only Lock Screen conflict audit of inherited OU GPOs, Registry.pol, GPP Registry.xml and optional remote HKLM.
- Added immediate remote install/cleanup of selected computers via Task Scheduler RPC, with machine membership checks and per-host results; no RSAT/WinRM.
- Preserved GPO GUID and security filtering during reuse, with new campaign ID, new SYSVOL source and computer version bump.
- Clarified that disabling a link does not stop existing local scheduled tasks.
- Expanded CI PowerShell parser checks for the three new scripts.
- This is preview code; end-to-end AD/GPO and remote RPC tests still required.

## 0.3.0-preview.1 — GPO lifecycle, conflicts and Push Now
- Read-only inventory of managed GPOs; Disable/Enable, Unlink/Relink, Reuse and guarded Delete.
- Reuse updates same GPO and increments machine AD/GPT version with rollback attempt.
- Conflict scanning: ancestor OU/domain GPO, Enforced/Block Inheritance, Registry.pol, GPP Registry, remote HKLM.
- Additional conflict scan before creating a new GPO; warnings and explicit confirmation.
- Immediate install/cleanup for selected domain computers via Task Scheduler RPC (no reboot, RSAT, WinRM).
- Computer membership and exact GPO GUID verified before remote actions.
- Added static and fixture tests, upgraded CI to parse all new PowerShell scripts.
- Windows runtime, AD and Task Scheduler operations still need integration testing.

# Development snapshot (2026-11-06)
- Removed RSAT and WinRM dependency from AD deployment: native Windows ADSI/LDAP and gpedit.dll COM.
- Computer security group filtering verified before linking the newly-created GPO.
- Credentials remain session-only, with both preview and confirmation.
- Windows integration test required; not yet production verified.

## 0.2.0-preview.2 (2026-11-06)
- No RSAT needed on administrator workstation: signed/sealed LDAP discovery.
- Remote Kerberos WinRM GPO preflight and deployment on chosen writable DC.
- Remote AD/GroupPolicy cmdlet availability errors explicitly surfaced.
- Maintains alternate session credentials without password in files/CLI.
- SYSVOL staging, file hash verification, OU and computer-group filtering.
- No TrustedHosts or Basic authentication configuration.

# Changelog

## 0.2.0-preview.1 (2026-11-06)
- Universal domain connection: writable DC discovery, alternate network account, OU/group and user search.
- AD/GPO wizard with read-only preflight and explicit deployment confirmation.
- Targeted GPO creation, security filtering, SYSVOL image upload and Startup PowerShell script metadata.
- Eliminated hardcoded production AD domain names from export and documentation.
- Self-contained Windows EXE remains the delivery format, RSAT required for domain administration only.


## 0.1.0 — development snapshot (2026-11-06)
- Initial WinForms editor with campaign calendar, image ordering, preview and project save/load.
- Embedded data-driven PowerShell agent for GPO Startup deployment.
- Registry backup / restoration, SHA-256 image checking and post-campaign cleanup.
- CI builds self-contained x64 and x86 single-file executables on Windows.
- Windows build, GUI and client runtime verification still pending.
