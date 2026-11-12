> **Preview.104 — read-only OU + security group coverage:** AD Deployment and Group assignments now show **total computer/user objects in the selected OU subtree** and **how many are direct members of the selected security group**, including a verified zero vs **Not verified** state. Counts come from capped, signed/sealed LDAP; group-first/All groups selections remain available. These are *not* effective GPO results: disabled/inactive accounts are included and nested/primary memberships, WMI, deny ACLs and RSoP are not resolved. [Preview.104 notes](docs/RELEASE-PREVIEW104.md) · [Scope guide](docs/OU-AND-SECURITY-GROUP-TARGETING.md).

> **Preview.103 — OU/Group Wizard in English, Russian and Hebrew:** localized OU-first/Group-first selection, independent User and Computer group assignments, read-only membership statuses, per-target warnings and GPO link help. The group-mode dropdown keeps its original selection keys, and the Group assignments window now wraps long translated guidance at 200% DPI. DN/GUID/LDAP/permissions are untouched. [Preview.103 What's New](docs/RELEASE-PREVIEW103.md) · [Targeting guide](docs/OU-AND-SECURITY-GROUP-TARGETING.md).

> **Preview.102 — native physical DPI / language check:** Tools → Visual DPI / language check... opens a scrollable in-app English/Русский/עברית pilot panel. It measures the actual window DPI, rejects remote/non-200% evidence, optionally runs the offline UI smoke, and permits saving unsigned *local-only* six-check operator observations. No separate PS1, network, screenshots or AD/GPO changes. The real Windows 200% UI and screen-saver domain acceptance gates remain open pending operator observation. [Visual acceptance guide](docs/WINDOWS-200-DPI-VISUAL-CHECK.md) · [Preview.102 notes](docs/RELEASE-PREVIEW102.md).

> **Preview.101 — safer OU/group selection:** A chosen USER or COMPUTER security group stays selected after picking or changing the target OU. When the group falls outside the direct-membership filter, the UI switches visibly to **All groups** instead of silently clearing it. Saved company group defaults and multi-row assignments are preserved. GPO Preview, rights/OU-link checks and deployment approval remain mandatory. [Preview.101 notes](docs/RELEASE-PREVIEW101.md).

> **Preview.100 — AD deployment OU and group targeting:** OU-first now filters direct computer security-group memberships (not groups stored in that OU); switch to All groups to pick a group first or include nested membership. Screen Saver uses User OU/User Group with its own optional direct-user filter. Same-name OUs show their parent paths; the SMB Browse button uses a separate row. AD lookups are read-only and cannot substitute for explicit GPO Preview or real endpoint gpresult. [Targeting guide](docs/OU-AND-SECURITY-GROUP-TARGETING.md) · [Preview.100 notes](docs/RELEASE-PREVIEW100.md).

> **Preview.99 — localized Repair results:** Repair outcome summaries, task counters, offline/pending/failed warnings and managed GPO-selection guidance are now translated in English, Russian and Hebrew. Original task IDs, repair queue/history, AD/GPO actions and manual approval gates remain unchanged. Includes reversible language and counter tests across simulated high-DPI layouts. [Preview.99 release notes](docs/RELEASE-PREVIEW99.md).

> **Preview.98 — compact numbered What's New:** the Updater and update-indicator tooltip now turn published GitHub Markdown into short, readable numbered changes. Raw release metadata, hashes, original GitHub text, manual installation approval and rollback remain unchanged. Russian and Hebrew headings are localized; offline formatting tests cover current and older releases. Windows x86/x64 CI must pass before the release is published. [Preview.98 release notes](docs/RELEASE-PREVIEW98.md).

> **Preview.97 — GPO/Client Health safety in three languages:** expanded Russian and Hebrew operator warnings for RSoP/Remote Registry inspection, privacy-sensitive CSV export, post-cleanup verification and Client Health Repair/report actions. Original AD paths, JSON, RSoP content, GPO identifiers and operation approvals remain unchanged. CI simulates six 100–200% WinForms font/scale profiles, not real 200% Windows desktop acceptance. [Preview.97 release notes](docs/RELEASE-PREVIEW97.md).

> **Preview.96 — Updater / Local Lab trilingual UI:** Russian and Hebrew translations now cover update checks, unsigned-publisher consent, rollback warnings, and local lab evidence privacy/safety messages. Release SHA-256, asset IDs, version, token, JSON evidence and GPO actions stay unchanged. Release channel choices are display-only, not new saved values. Six-scale 100–200% simulated WinForms acceptance is not a real Windows desktop DPI test. [Preview.96 notes](docs/RELEASE-PREVIEW96.md).

> **Preview.95 — Russian/Hebrew Advanced UI:** gMSA setup/readiness, SMB feedback/alerts and Device & Repair now have localized controls and safety warnings in **Русский** and **עברית**, with English fallback. GPO/agent IDs, queue states, technical reports and export schemas remain unchanged. Manual Repair still requires the existing Preview and full GPO GUID. Windows x86/x64 simulated 100–200% layout checks do not replace real interactive testing. [Preview.95 release notes](docs/RELEASE-PREVIEW95.md).

> **Preview.94 — English / עברית / Русский:** Interface Settings now offers all three languages. Russian uses LTR and Hebrew uses RTL; translations cover the campaign Wizard, AD/GPO, Client Health, schedule editor and many Advanced dialogs. Technical fields/CSV/JSON/GPO commands remain canonical English and do not change when switching UI language. This is still **partial localization**, with real interactive Windows 10/11 200% DPI acceptance pending. [Preview.94 release notes](docs/RELEASE-PREVIEW94.md).

> **0.4.0-preview.61 — native C# link-only GPO Apply:** `Disable`, `Enable`, `Unlink`, and `Relink` are handled by native signed/sealed LDAP. On Apply, the full selected-DC lifecycle preview is freshly rebuilt (managed campaign identity, AD and SYSVOL versions, OU/site link inventory, package checks as applicable); exact approved GPO GUID and case-sensitive preview SHA-256 must match. A single LDAP ModifyRequest deletes the exact previously read `gPLink` value and adds its replacement, then reads back the result. Concurrent edits cause the stale-value delete to fail instead of blindly overwriting the link. **GPO Delete and Reuse are still handled by the audited PS1 path**, as are Deployment Deploy, remote Push/Repair, and SYSVOL Cleanup. This update requires Windows CI compilation and real isolated-OU testing before production rollout; no live domain tests or published EXE are claimed.

> **0.4.0-preview.60 — audited PS1 migration boundary:** Native C# is mandatory for AD Discovery, DC Consistency, Computer & Group Assignment, basic Conflict Scan, SYSVOL Scan/Preview, GPO Lifecycle List/Preview, Immediate List/Preview/VerifyCleanup and Deployment Plan. Embedded legacy PowerShell is now explicitly allowlisted **only** for Deployment Deploy; Lifecycle Apply; Conflict Effective/VerifyEndpoint/VerifyEvidence; Immediate Push/Cleanup/Report/Repair; and SYSVOL Cleanup. Unknown modes fail closed instead of launching a PS1. This is a dispatch hardening step, **not** a conversion of the remaining write operations. No live DC validation or completed x64/x86 CI build is claimed.

> **0.4.0-preview.59 — AD connection permission assessment:** After a signed/sealed read-only AD inventory, Connection Review performs a native C# LDAP ACL preflight for GPO creation and, when configured, the profile's default OU link permission. It resolves the selected AD account's SID and computed `tokenGroups` using a direct base-scope query. Incomplete group-token data is shown as **Unknown**, never as a confirmed denial; ACL matches are advisory, not effective access proof. No test policies, directory changes, or SYSVOL writes occur. The deployment wizard still independently verifies selected OU/GPO scope and write access before making changes. Remaining administrative PS1 Apply paths have not yet been converted and are not claimed as native. Windows CI and live domain validation are still required.

# LockScreen Campaign Manager

> **Preview.90 — Hebrew UI foundation and private Windows lab evidence viewer:** In **Tools → Settings**, switch the operator interface between **English** and **עברית (Hebrew)**, with right-to-left form layout, a translated core action/navigation dictionary, and original left-to-right technical identifiers/UNC paths/JSON/log data. The setting persists per Windows user. Missing translations remain English in this first localization stage; reopen windows when changing language to refresh dynamic content. In **Tools → Review local lab evidence** or **GPO Operations → Lab evidence**, import the read-only, private **SMB LocalSystem canary** and **Endpoint closure** JSON reports, independently and with strict schema/size/consistency checks. Stale or unsigned data is never claimed to prove online status or a visible lock screen. [Preview.90 release notes](docs/RELEASE-PREVIEW90.md).


> **Preview.89 — SMB preflight evidence you can trust:** For a dedicated SMB campaign source, **Preview / check conflicts** now reports the exact UNC source and per-target package path; the read-only NTFS broad-write Allow-ACE scan is labeled *ADVISORY*, while share permissions, actual computer-account READ and SMB availability at startup are explicitly **NOT TESTED**. Unrelated Smart SYSVOL duplicate-image scans are **skipped** for SMB packages. These limitations appear again before approving new GPO creation, for both single and multi-group deployment. Real Windows LocalSystem pilot acceptance remains mandatory. [Release notes](docs/RELEASE-PREVIEW89.md).


> **Preview.88 — consistent SYSVOL/SMB save workflow:** Select campaign storage on **Step 6 Save / Skip** before saving a project, or change it on **Step 7 Deploy**. A shared chooser keeps the UNC path in the in-memory project. If the storage selection changes *after* an existing save, a prominent warning and **Save project changes...** action prevent the previous JSON project silently being treated as current. You may explicitly continue without updating the old file only after confirmation. Offline/no-save workflows still work. See [release notes](docs/RELEASE-PREVIEW88.md).


> **Preview.87 — Wizard source selector + HiDPI Update UI fix:** The main seven-step Campaign Wizard now offers **Campaign files → SYSVOL (default) / SMB network folder (UNC)** on step **7 Deploy**, with an editable UNC path and Browse. This choice persists in the project and follows into the AD / GPO deployment Wizard; subsequent edits there also return to the editor. The Updater's Release channel and credential fields now use DPI-aware measured row heights, fixing the clipped `Preview + stable` selector at 125–200% display/font scale. Invalid UNC paths block opening deployment without attempting AD changes. [Release notes](docs/RELEASE-PREVIEW87.md).


> **Preview.86 — independently confirm client cleanup:** a read-only LocalSystem test on an isolated Windows 10/11 computer now takes a **pre-deployment registry fingerprint** and later checks the restored registry, exact Computer GPO RSoP, scheduled tasks, ownership, local files and final postcleanup receipt. A client `Completed` status alone is not enough. This test does not mutate domain policy and cannot replace actual display/boot acceptance. [Local closure audit instructions](docs/ENDPOINT-CLOSURE-AUDIT.md).


> **Preview.85 — SMB post-reboot and Computer GPO evidence:** The read-only LocalSystem canary can now compare the test client's last boot with the new deployment receipt and check the exact **Computer GPO GUID** in local `gpresult /x` output (without retaining private XML). The results distinguish package accessibility, RSoP applicability and manual visual/rollback acceptance. This is **not** a production-domain test or proof of lock-screen display. See [Local canary guide](docs/SMB-SYSTEM-CANARY.md).


> **Preview.84 — SMB under LocalSystem verification:** after deploying a test campaign with protected SMB storage, use the offline kit's read-only LocalSystem canary on a disposable Windows 10/11 computer to verify share access and immutable package hashes. The Manager stores pinned source SHA-256 in deployment receipts and rechecks the full SMB package before GPO startup registration. This does **not** prove boot-time GPO application or physical lock-screen display; those still require real endpoint acceptance. [SMB SYSTEM canary instructions](docs/SMB-SYSTEM-CANARY.md).


> **Preview.83 — choose SYSVOL or protected SMB network storage:** On AD / GPO Deployment, select **Campaign files → SYSVOL (default)** or **SMB network folder (UNC)**, then enter an existing dedicated \\server\\share\\folder. GPO policy objects and startup metadata **always** remain in AD/SYSVOL; the immutable agent/images/manifest package may live outside SYSVOL in an administrator-controlled read-only-for-computers SMB share. Read-only preflight verifies the UNC, local NTFS ACLs and package uniqueness. It cannot prove share-level ACL, machine-account access, DFS/network availability at boot, or actual RSoP; test on one dedicated machine first. SMB package cleanup is manual after GPO unlink and endpoint cleanup. [Details](docs/SMB-CAMPAIGN-SOURCE.md).


> **Preview.80 — real JPG photo Screen Saver local pilot:** The Campaign Type Wizard now offers **JPG photo slideshow**, preserves image/arrange/schedule steps, and exports a portable **PhotoScreenSaver.scr** package containing normalized JPEGs and SHA-256 manifests. The SCR is a restricted self-contained apphost with /s, /p, /c and offline --photo-saver-self-test modes. JPG **automatic AD GPO deployment is deliberately blocked** pending a real Windows 10/11 /s and /p client test and a verified user-scope delivery mechanism. Existing built-in Windows .scr campaigns are unchanged. [How to export and test](docs/JPG-SCREEN-SAVER-PILOT.md).

> **Preview.79 — complete group GPO candidate report:** After Group assignments → Preview / check conflicts, open **Full conflict report...** to review EVERY scanned User/Computer policy entry (up to 500 per target) with search, risk filter, selected policy provenance/coverage notes and an opt-in, formula-safe local CSV export. Previous compact Preview displayed only the first 12 results. The full report is read-only, reset on scope changes, and **does not imply client RSoP**. No GPO changes in the report itself.

> **Preview.78 — strict MS-GPREG conflict evidence:** The read-only per-target scan now rejects malformed `Registry.pol` key and value identifiers with missing UTF-16LE NUL terminators, truncated numeric fields and unterminated text data. Previous fixture tests accepted malformed records. No existing GPO or client is modified. See [Screen Saver group targeting](docs/SCREEN-SAVER-GROUP-TARGETING.md).

> **Preview.77 — safe GPO half inventory and Screen Saver UI path:** An unused User/Machine policy half can legitimately lack a SYSVOL directory; the scanner now cross-checks AD GPO version and matching extension list before deciding whether missing files are harmless or a **blocking inconsistency**. It also scans `NoDispScrSavPage` at Microsoft's documented HKCU Policies\\System key. No domain changes or hidden overrides. See [Screen Saver group targeting](docs/SCREEN-SAVER-GROUP-TARGETING.md).

> **Preview.76 — scoped Computer/User GPO conflict preflight:** Group-assignment Preview now independently scans the chosen DC's inherited/linked GPO registry policy files for each Computer Lock Screen or User Screen Saver OU. It reports static overlaps and unknown coverage, fails closed on read errors, and requires another default-No acknowledgement when overlaps are found. This is not effective client RSoP; on a pilot verify `gpresult` and WMI/security/site scope before production. [Scope and limitations](docs/SCREEN-SAVER-GROUP-TARGETING.md).

> **Migration/publication:** new identity is not an in-place migration of existing deployed agents or GPOs. Follow [universal migration and clean-history publication guidance](docs/UNIVERSAL-MIGRATION.md); do **not** switch the historical repository to Public.

**Current preview:** `0.4.0-preview.104` — portable .NET 10 WinForms campaign manager for **Windows 10 and Windows 11** (x64/x86). Windows 10 Enterprise/Education/IoT Enterprise client agents support Windows 10 1607+ (including supported LTSC editions); Windows 11 Enterprise/Education/IoT Enterprise is supported too. The manager can edit projects offline without admin rights. Read-only AD inventory and optional GPO deployment are separate workflows. See [manager OS support](docs/MANAGER-OS-COMPATIBILITY.md) and [Windows 10/11 client support](docs/WINDOWS10-CLIENT-COMPATIBILITY.md). Actual Windows 10/11 domain-connected endpoint testing remains required.

> **Public repository — clean source history.** The former institution-specific repository must remain private until explicitly retired. This new project contains only a reviewed source snapshot and its own new commits. Unsigned portable EXEs require manual approval; do not treat a checksum as a publisher signature.

> **0.4.0-preview.28 development — AD-first onboarding:** On startup, **1 Connect to DC & Read AD** reads signed LDAP RootDSE, DCs, OU, computer security groups and GPO metadata before proceeding to the campaign editor. New BitKeyBridge-style navy/blue/green GUI, explicit offline mode (no deployment), and a newly visible DC read-only status. [Details](docs/AD-FIRST-CONNECTION.md). The published preview.27 is unchanged; this change is not a production release.

> **0.4.0-preview.27 — RSoP CSE + Image Integrity:** In **GPO Operations → Lock Screen conflicts → Analyze effective (PC)**, click **Verify RSoP + image** to perform a separately approved AD-scoped read of existing Windows Computer Registry Policy RSoP (WMI/DCOM) and check a managed JPEG's SHA-256 over read-only SMB against the selected DC campaign manifest. **Precedence=1** is labeled *CSE reported winner (cached)*; hash equality is reported independently. Neither proves the physical lock screen display. No remote scripts, GPO changes, automatic retries or uploads. [Details](docs/RSOP-CSE-IMAGE-INTEGRITY.md).

> **0.4.0-preview.26 — Actual Endpoint Observation:** In **GPO Operations → Lock Screen conflicts → Analyze effective (PC)**, select **Verify endpoint HKLM** and explicitly approve a read-only native 64-bit Remote Registry snapshot of the revalidated AD computer. The **Winning policy analysis** window now compares actual observed values (including MDM PersonalizationCSP) with estimated static GPO priority and marks matches/mismatches/absent/unreadable/offline results without ever claiming a verified GPO origin or physically displayed lock screen. No remote changes, automatic collection, RSAT or WinRM. [Details](docs/ACTUAL-ENDPOINT-OBSERVATION.md).

> **0.4.0-preview.25 — Winning Policy Analysis:** In **GPO Operations → Lock Screen conflicts → Analyze effective (PC)**, choose **Winning policy analysis**. A new read-only per-setting report compares estimated static GPO priority from the **actual computer OU**, Link Order and Enforced, and optionally shows imported GPO-level gpresult XML observations. It distinguishes conflicting values, unknown GPP ordering, uncertain Site/security and **never claims a verified client winner**. Private CSV export is manual only; no real DC/Windows 11 test was performed. [Details](docs/WINNING-POLICY-ANALYSIS.md).

> **0.4.0-preview.24 — RSoP Evidence Import:** Within **GPO Operations → Lock Screen conflicts → Analyze effective (PC)**, optionally **Import gpresult XML** generated on the actual Windows 11 computer. Compare selected-DC AD candidates by GUID with computer-scope gpresult flags; see per-GPO **Reported applied/filtered**, **Insufficient flags** or **Not listed (unknown)**, report time and SHA-256. The original local XML stays private and in memory only. No new RPC/PowerShell on endpoints; no automatic data upload. See [RSoP Evidence Import](docs/RSOP-EVIDENCE-IMPORT.md). Hosted CI only; stable unchanged.

> **0.4.0-preview.23 — Effective GPO Conflict Analysis:** In **GPO Operations → Lock Screen conflicts**, select a managed campaign GPO, type a domain computer FQDN and click **Analyze effective (PC)**. The read-only report checks AD OU+security-group membership, inherited/Enforced/blocked links, ACL/`tokenGroups` hints, presence of WMI filters, potential Site GPO and static LockScreen settings. **It is not client RSoP.** The existing **Scan OU + GPO** remains available. See [Effective GPO guide](docs/EFFECTIVE-GPO-CONFLICTS.md). Hosted CI only; real DC/Win11 testing deferred.

> **0.4.0-preview.22 — DC Consistency Audit:** **GPO Operations → DC Consistency** compares explicitly selected domain controllers (up to 12 at a time) using RootDSE UTC, campaign manifest/agent/images SHA-256, startup script and AD/GPT version. Detect clock drift, missing SYSVOL content, mismatches and inaccessible DCs without any remote writes. Partial coverage never reads as a domain-wide pass. See [DC Consistency guide](docs/DC-CONSISTENCY-AUDIT.md). Hosted CI only; real DC test deferred.

> **0.4.0-preview.21 — Post-Cleanup Verification:** in **GPO Operations → Device & Repair Dashboard → Closure audit**, choose 1–25 AD-verified PCs and click **Verify selected (read-only)**. Remote SMB/Task Scheduler/Registry values are observed without scheduling tasks, RPC repairs or GPO changes; old/unknown PCs stay pending for manual rechecks, and results persist only in the admin's local profile. The agent produces a separate receipt *after* cleanup. This is observational evidence, **not** a signed attestation or an RSoP guarantee. See [Post-Cleanup Verification guide](docs/POST-CLEANUP-VERIFICATION.md).

> **0.4.0-preview.20 — Campaign Closure Audit:** From **GPO Operations → Device & Repair Dashboard**, open **Closure audit** after loading verified AD clients and fresh feedback. The audit separates selected-DC GPO link state from agent self-reported completion and unresolved User/gMSA Repairs. Filters, per-device explanations and local opt-in CSV are provided. **Reported completion is not proof of task/file/registry cleanup.** See [Closure audit guide](docs/CAMPAIGN-CLOSURE-AUDIT.md). No live DC validation yet.

> **0.4.0-preview.19 — durable Repair history:** GPO Operations → Device & Repair Dashboard now shows persisted User/gMSA queue transitions, including cancelled, interrupted and health-confirmed Repairs. Click **Repair history** for a searchable read-only timeline and manually approved local CSV. Sensitive journals remain in the administrator profile or protected gMSA directory; the timeline is not an RPC audit or physical lock-screen attestation. See [Repair history](docs/REPAIR-HISTORY-RU.md). No domain-side validation yet.

> **No internal GitHub runner?** Use the [Offline AD + Windows 11 Lab Kit](docs/OFFLINE-AD-WIN11-LAB.md). GitHub Actions builds and verifies the downloadable ZIP using hosted Windows; run Preflight and optionally Full on your own domain-joined management workstation. The real domain test is not performed by GitHub, and local results are never uploaded automatically. The self-hosted workflow remains optional, not required.

> **0.4.0-preview.17 — real Windows 11/DC pilot harness:** A new manually triggered **Live AD + Windows 11 Lab** workflow can test an isolated GPO on a dedicated self-hosted domain-connected runner, with remote Windows 11 startup, image rotation and cleanup evidence. Ordinary GitHub Windows runners remain offline; this needs the private `ad-lab` environment, a labelled runner, 1–3 dedicated lab PCs and explicit approval. See [live lab setup](docs/LIVE-AD-WIN11-LAB.md). No production DC/PC test is implied by green ordinary CI.

> **0.4.0-preview.15 — safer image matching and AD primary-group handling:** A weekday-only or date-only filename is not automatically assigned if it fits multiple scheduled changes; unique numbered filenames remain eligible. Computer & Group Assignment now reports **Primary** group membership and blocks direct Add/Remove for it. Both behaviors have offline regression coverage; live AD validation is still required. See [Image Import](docs/IMAGE-IMPORT-RU.md) and [Computer Assignment](docs/COMPUTER-GROUP-ASSIGNMENT-RU.md).

> **0.4.0-preview.14 — safety fixes:** Computer & Group Assignment now fails closed on unverified LDAP group USN/writable DC or protected group SID and rechecks computer identity before membership writes. Smart image-folder import requires manual review for multiple/malformed dates and conflicting image ordinals. See [Computer Assignment](docs/COMPUTER-GROUP-ASSIGNMENT-RU.md) and [Image Import](docs/IMAGE-IMPORT-RU.md).

> **0.4.0-preview.13 — smarter campaigns:** Choose **Companies → New campaign → Schedule** to select weekdays, exact dates or every N hours/minutes, then **Import image folder...**. Weekdays/numbers/dates in filenames are mapped to image slots, with manual review for ambiguity. **GPO Operations → Computer & Group Assignment** searches the selected OU and safely previews a single direct security-group membership Add/Remove with typed confirmation. No GPO or client changes happen from selecting a company or importing images. See [image import](docs/IMAGE-IMPORT-RU.md) and [group assignment](docs/COMPUTER-GROUP-ASSIGNMENT-RU.md).

> **0.4.0-preview.12 — flexible image timing:** In a company campaign, select images and click **Schedule...** to choose Every day, Work days, Custom weekdays or Specific dates. Assign an exact date/time to each image, repeat an image on multiple days, mark change dates in the calendar, and optionally use a deterministic per-PC image order with a sample-device preview. Image changes run from the existing client SYSTEM task; nothing is changed in GPO merely by editing. See [schedule guide](docs/IMAGE-SCHEDULE-RU.md).

> **0.4.0-preview.11 — multi-company workspaces:** Use **Companies → New company** to store separate non-secret AD domain, preferred DC, OU/group, Client Health share and time-zone defaults, then **Select company → New campaign**. The GUI never copies a previous company's password, silently changes GPO, or treats a saved OU/group as proof of membership. See [multi-company guide](docs/COMPANY-PROFILES-RU.md).

> **0.4.0-preview.10 — GitHub Actions verification:** The Windows x86/x64 build now runs `--ui-smoke-test` against four management dialogs under multiple simulated font scales. The diagnostics-review workflow also runs automatically when its implementation changes on `main`, using read-only permissions. This checks CI mechanics, not actual domain/gMSA or visible high-DPI screenshots.

> **0.4.0-preview.9 — private GitHub releases:** **Updates** now accepts an optional short-lived read-only session token, held in the dialog only and sent exclusively to a pinned GitHub API repository. Private release ZIPs are fetched through numeric asset IDs; redirects to GitHub's CDN never receive the token. The EXE is staged after SHA-256, ZIP and PE architecture validation and checked with native WinVerifyTrust Authenticode. **No unattended EXE replacement or rollback** is enabled while gMSA runs. The **Backup status** button inspects an existing local .bak checkpoint without changing it. See [updates guide](docs/UPDATES-RU.md).

> **Windows GUI layout:** The main window already remembers its position; 11 additional dialogs now use the shared DPI-aware window geometry module. Closing and reopening restores size and monitor position, clamped to visible work areas when displays change. See [UI guide](docs/WINDOW-PLACEMENT-RU.md).

> **GitHub diagnostics review:** Published UniversalDiagnostics v1 Issues are aggregated by a read-only [scheduled workflow](.github/workflows/diagnostics-review.yml). It runs daily or on new/edited Issues, and produces an aggregate artifact and Actions summary. Only operator-published Issues are analyzed; the manager does not upload logs automatically. See [diagnostics](docs/DIAGNOSTICS.md).

> **Local error trends:** Open **Diagnostics → Error trends** to group explicitly saved error reports by fingerprint, count repeated failures and export an aggregate JSON after review. Files are validated locally; no automatic GitHub upload or background error collection. See [diagnostics](docs/DIAGNOSTICS.md).

> **Production Readiness / Device evidence (preview.8):** Open **GPO Operations → gMSA 24/7 → Production Readiness**, or **Configure gMSA → Readiness**. Eleven safe read-only checks distinguish "ready for pilot" from a real, independently verified domain deployment. **Device & Repair Dashboard → select computer** now displays the most recent durable Repair attempt metadata and the agent's own health claims. JSON export is opt-in and contains neither usernames, AD paths nor raw client files. See [readiness guide](docs/PRODUCTION-READINESS-RU.md).

> **Device & Repair Dashboard (0.4.0-preview.8):** Open **GPO Operations → Device & Repair Dashboard**, select a managed GPO, load verified computers from the current OU and security group, then inspect the joined Client Health, Repair queue and local history states. Image names and integrity flags are reported by the endpoint agent, **not proof that an image was visible on the screen**. Repair Selected always uses the existing Preview + full GPO GUID confirmation and blocks unresolved disabled-gMSA queue tasks. See [dashboard guide](docs/DEVICE-DASHBOARD-RU.md).

> **Preview 0.4.0-preview.8:** The **Updates** button embeds the canonical UniversalUpdater core with opt-in GitHub Releases checks and verified download staging (no automatic installation). The repository is private, so unauthenticated checks may not see its Releases. See [updates guide](docs/UPDATES-RU.md). UniversalDiagnostics remains local/opt-in only.

# Previous development notes

**Merged preview:** GPO Lifecycle + Client Integrity & Self-Healing + Client Health, Repair Selected, opt-in alerts and local campaign history.

# LockScreen Campaign Manager

Native-looking Windows Forms administration tool for recurring Active Directory Lock Screen image campaigns.

## Version / status

**Earlier preview features: private updater integration, gMSA management and Device & Repair Dashboard. Live Windows/AD testing pending.**

- C# / **.NET 10 WinForms**, no third-party NuGet packages.
- Publish `win-x64` or `win-x86` as a **single-file self-contained EXE**. End users do not install .NET.
- Add JPG/PNG/BMP images, reorder or replace them, preview the selected image.
- Choose campaign name, start date, local change time, Windows time zone and duration of final image.
- Number of images controls duration: one image per day, at the same local time.
- Export full GPO package: `campaign.json`, validated JPEG files, `LockScreen-Agent.ps1`, and a deployment guide.
- PowerShell 5.1 agent supports `-Mode Install`, `-Mode Repair`, `-Mode Apply`, `-Mode Cleanup`, `-Mode Status`, `-Mode Report`, `-Mode Test`.
- Agent saves original `LockScreenImage` and `NoChangingLockScreen` values before applying anything.
- Registers a local SYSTEM task with an event for each image, one final cleanup trigger and a boot-recovery trigger.
- A GPO Computer Startup script is **only the installation method**. The scheduled task is local, created by the agent.
- Repeated GPO Startup executions are idempotent for an unchanged campaign; expired campaigns do not reinstall.
- After completion, local task, images, script and backups are removed. Windows Event Logs and OS caches are untouched.
- Screen and window dimensions are remembered between GUI runs.

## Screenshot / development

No verified Windows GUI screenshot is included. GitHub Actions compiles x64/x86 and runs isolated tests, but real GUI/DPI and domain integration testing remain pending.

## Building

Run `build-release.cmd` on Windows with Visual Studio 2026 or .NET 10 SDK, or push to GitHub and open the **Build portable EXE** workflow.

The workflow runs on `windows-latest`, checks PowerShell parser syntax, publishes two standalone EXEs and runs basic schedule checks. Downloads appear in the workflow run's Artifacts section.

## Deploying with GPO (any AD domain)

Use **AD / GPO Deploy** from the GUI to connect to any reachable Windows Active
Directory domain (current Windows account or an alternate network identity),
choose a writable DC, browse/search OUs and security groups, preview changes and
create a targeted new GPO with its Startup script and SYSVOL files.

The program automatically constructs paths using the selected domain and DC;
it does **not** contain a built-in production DNS domain or hardcoded OU.

The GUI EXE is self-contained. **No RSAT anywhere and no WinRM required.**
Discovery uses signed/sealed LDAP; deployment uses Windows ADSI and native
Gpedit.dll COM to create a new GPO on the selected writable DC. No administrative
PowerShell modules or remote management services are required. Clients
need neither RSAT nor .NET. If COM/LDAP is blocked, export the package and deploy
manually through an authorized administrative environment; the GUI fails closed.

See [Universal AD deployment](docs/AD-DEPLOYMENT.md) for permission and targeting
details. For manual GPO setup see [GPO Startup instructions](docs/GPO-Setup-RU.md).

## Example schedule

Five slides starting Sunday 2026-11-08 at 14:00 Israel time:

| Image | Start |
|---|---|
| 01.jpg | 2026-11-08 14:00 |
| 02.jpg | 2026-11-09 14:00 |
| 03.jpg | 2026-11-10 14:00 |
| 04.jpg | 2026-11-11 14:00 |
| 05.jpg | 2026-11-12 14:00 |

Restoration / cleanup: **2026-11-13 14:00 Israel time**.

## Notes on reliability

- GPO startup scripts are executed at system startup, not on a simple `gpupdate /force`.
- Windows updates a visible Lock Screen on lock/logon; an image changed while already locked may not appear immediately.
- By design, the exporter refuses reusing an existing output folder. Use a fresh campaign ID to avoid unintended overwrites.
- An image's SHA-256 is embedded in `campaign.json` and checked by the agent prior to applying.
- The Windows agent needs elevated privileges. The GUI needs no such privileges for offline packaging.
- Automatic local cleanup does **not** modify or delete the domain GPO itself or its SYSVOL source files.
- Smoke tests cannot replace real Windows 11 Enterprise/Education validation before a broad deployment.

## Architecture

`src/LockScreenCampaignManager/` contains the .NET WinForms GUI and embedded PowerShell agent. No external runtime required for published EXEs. Each campaign has an independent ID and local Task Scheduler entry.

## 0.3.0-preview.1 — GPO lifecycle and immediate client rollout

The universal no-RSAT GPO administration UI supports disable/enable OU links,
unlink/relink, reuse with a **new campaign** and GPO deletion after link validation.
Read-only conflict analysis inspects ancestor and OU links, `Registry.pol`,
GPP registry XML and optionally remote HKLM. A new campaign preview automatically
runs the conflict scan before creating a new GPO.

Push Now uses restricted, approved Remote Task Scheduler RPC to install or clean
up an already defined agent on up to 25 selected computers (same OU + security
group), without reboot or WinRM. All mutations require a matching preview hash
and manually confirmed GUID.

See [Administrator guide (Russian)](docs/GPO-LIFECYCLE-RU.md). This is a source
preview: static tests pass; Windows compilation and real domain/RPC operations
remain unverified in the current execution environment.


## GPO lifecycle, conflict scan and immediate push (0.3.0-preview.1)

From **AD / GPO Deploy**, connect to a domain, select a computer OU and security group, then open **Управление GPO / конфликты / Push Now**. The three tabs provide:

- **GPO actions**: List app-managed policies; Disable/Enable just the selected OU link; Unlink; Reuse the existing GPO with a **new campaign ID** and preserved filtering; Delete only when no domain/OU links remain. All writes require a read-only Preview and exact GPO GUID confirmation. Existing local scheduled tasks will **not** stop just because the GPO link is disabled.
- **Conflict audit**: Read OU/ancestor links, Block Inheritance/Enforced flags, machine `Registry.pol` and `Preferences/Registry/Registry.xml`, plus optional remote computer HKLM. The report marks uncertain RSoP dimensions; it does not claim to compute effective policy in all cases.
- **Push Now**: Select up to 25 AD computer accounts belonging to both the chosen OU and group. Create and run an on-demand remote SYSTEM scheduled task through Task Scheduler RPC (no WinRM), with per-PC result and the option to perform remote Cleanup. Before the campaign start date, Push only installs the agent.

The program is not yet proven against a real AD and Windows fleet. Do not use these powerful lifecycle operations in production until tested on isolated OU/computers. See [maintenance guide](docs/GPO-MAINTENANCE-RU.md).

## Client Feedback / Live

Optional client feedback requires only a dedicated writable SMB folder configured by the administrator; no additional server service, WinRM or RSAT.
New or reused campaigns can include a feedback share, for example `\\fileserver\LockScreenReports$`. Windows 11 agents write per-computer compact JSON (no user names, passwords or registry contents) every 5–60 minutes and at relevant changes, even if the Manager is closed. When open, **Client Health / Live** refreshes its dashboard every 15 seconds, compares reported machines with AD OU+group targets and shows healthy/pending/error/stale/no-report counts. Do not use SYSVOL for telemetry. Operators configure SMB/NTFS ACLs separately. See [CLIENT-FEEDBACK-RU.md](docs/CLIENT-FEEDBACK-RU.md).

The x64/x86 EXE is self-contained; no installed .NET needed on the admin PC. Agents are Windows PowerShell 5.1 scripts and need no installed .NET runtime.

**Status:** Windows CI compilation and full AD integration must be validated; this preview must first be tested on a separate OU. The current production GPO and any already exported campaign packages remain unchanged.


## Client management (0.4.0-preview.3)

- **Repair Selected:** select 1–25 computers in Client Health; the manager re-resolves each name from the AD OU/group intersection, checks the managed GPO CampaignID, previews a Task Scheduler RPC repair and demands the exact GPO GUID before running it. No RSAT or WinRM.
- **Local alerts:** opt-in Windows tray balloon when the number of problematic computers increases beyond a configurable threshold, with a cooldown. Initial reading sets the baseline; no automatic remote repairs based on uploaded SMB reports.
- **History:** local event timeline for export, GPO changes and remote actions; manually archived health snapshots and final HTML/UTF-8 CSV reports with one row per computer. A final report can be incomplete; the counters remain explicit.

See [Client Management user guide (Russian)](docs/CLIENT-MANAGEMENT-RU.md).

**Data handling:** Only the client JSON heartbeat is stored in the dedicated SMB share. Admin-side History is stored in `%LOCALAPPDATA%\LockScreenCampaignManager\History`; it may contain domain/OU names and device names, so review before sharing. No automatic upload to GitHub or background network telemetry is introduced.

**Validation:** Windows Actions compile both autonomous EXEs and run schedule, SHA256 self-healing, feedback, offline GPO regression, alert and history tests. Real AD GPO creation/remote Task Scheduler permissions/SMB ACLs require a dedicated test OU before production.

## Deferred Repair: save requests for offline PCs

After an explicitly approved Repair Selected action, hosts returning a recognized RPC
offline result are saved in a local per-user queue. This is NOT a background service.
- Deferred Repair tab lists each host, pending status, next retry and reason.
- When the connected manager remains open, it checks every 60 seconds; transient
  failures back off 4 / 8 / 16 / 30 minutes, up to 20 attempts or 14 days.
- Each attempt freshly verifies OU + security-group eligibility, the managed GPO,
  unchanged CampaignID and action preview hash before remote execution.
- Unknown/pending remote task results and authorization/ACL errors stop automatic retry.
- No domain passwords are saved in the queue. Cancel or manually retry selected jobs.
- When the manager is closed, GPO Startup and the installed agent's local repair
  remain independent fallback mechanisms.

Documentation: docs/DEFERRED-REPAIR-RU.md.

## Deferred Repair: confirm the client, not just RPC

After remote Task Scheduler returns exit code 0, a deferred repair is **AwaitingHealth**
rather than immediately marked successful. While the manager is open, it polls the
configured dedicated SMB feedback share for a report from that exact AD computer
and the same campaign, newer than the remote repair submission time.
Only an Active/Pending **Healthy** report with verified file hashes, expected
registry (when active) and a local campaign task removes the queue item.
A stale, missing or unhealthy report never automatically repeats the remote task;
the job becomes NeedsAttention after two hours if still unconfirmed.
The remotely run task's exit code is not a proof of a visible lock screen.
Client reports are untrusted hints and should be validated on a test endpoint.


## Repair lifecycle tracking (0.4.0-preview.8)

Every initially approved Repair result is persisted: verified offline RPC goes to Queued; remote task exit 0 goes to AwaitingHealth; an uncertain remote task goes to AwaitingTask with NO automatic RPC retry. Fresh healthy SMB status completes a pending request. Before a queued offline RPC retry, the manager also checks whether the local agent recovered the endpoint automatically at boot. A lost RPC connection after task registration was attempted is ambiguous and never classified safe to retry. No domain password is stored. Automatic RPC retries still require the manager's GPO Operations window and active authenticated session; no background service is installed. End-to-end validation in a test AD is still required.


## Background Repair Worker (0.4.0-preview.8)

In **GPO Operations → Отложенный Repair**, opt in to the built-in Windows Task Scheduler background worker. It runs every two minutes as the **current interactive Windows account**, even while the GUI is closed, but only while that account is logged on (not a Windows service, not an S4U task and no stored password). It processes at most one previously approved RPC retry per cycle and checks up to 20 protected SMB feedback reports. Background execution is a separate opt-in; existing queued items and jobs created with alternate credentials remain foreground-only. Restart, cancellation and ambiguous tasks remain safe; cross-process queue writes are mutex-protected. The worker does not create new GPOs or expand OU/group scope. Live Windows Task Scheduler permissions and AD integration must still be tested before production deployment.

See the [Russian Background Repair setup and safety guide](docs/BACKGROUND-REPAIR-RU.md).


## Unattended gMSA 24/7 Worker (0.4.0-preview.8)

An optional, separately installed, passwordless gMSA Scheduled Task runs on an always-on, domain-joined Windows host, even when ALL administrator interactive sessions are logged off. The worker uses a protected machine-wide queue under ProgramData, not the approving user's LocalAppData. After Preview and exact GPO GUID confirmation, the administrator can opt to route new Repair requests into the gMSA queue using the dedicated **gMSA 24/7** tab. The worker rechecks approved AD OU, nested computer security group, managed GPO and campaign ID on each RPC attempt; it does not create GPOs, store passwords or require RSAT/WinRM during runtime. Queue and EXE paths are installed with restricted Windows ACLs. New setup is documented in docs/GMSA-REPAIR-RU.md; one-time installer: GPO Operations → gMSA 24/7 → Configure gMSA. Windows/AD/gMSA live integration is still required before production.


## Native gMSA setup (0.4.0-preview.8)

The one-time gMSA worker installation no longer uses PowerShell scripts. Run the published EXE **as local administrator** on the permanently online, domain-joined worker host. Go to **GPO Operations → gMSA 24/7 → Configure gMSA**. The native dialog checks the existing AD-managed account and local host availability, then installs the account locally (when already authorized by AD), secures ProgramData EXE/queue/config using NTFS ACLs, and registers a passwordless Scheduled Task using Windows COM. Buttons support Check, Install/Repair, Disable and Remove Task. AD gMSA creation and password-retrieval host permissions must exist before installation. The program never creates the AD account or stores a password. A dedicated offline test OU and real Task Scheduler verification remain required.

## Read-only gMSA diagnostic (0.4.0-preview.8)

From **GPO Operations → gMSA 24/7 → Configure gMSA**, click
**Run gMSA test**. The installer creates a dedicated on-demand Task Scheduler
task `LSCM-Gmsa-ReadOnlyDiagnostic` under the same AD-managed gMSA. It runs a
headless `--gmsa-diagnose-once` branch in the protected EXE, checks SID,
NTFS ACLs, read-only queue access, domain DNS, SYSVOL and Client Health share,
then writes a local protected report. It **never triggers remote RPC Repair,
writes GPO changes or creates client tasks**. Task registration alone is not
proof of a successful gMSA sign-in. Physical host / domain test remains required.
