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
