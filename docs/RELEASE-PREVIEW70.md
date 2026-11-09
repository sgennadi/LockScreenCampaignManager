# LockScreen Campaign Manager 0.4.0-preview.70 — Agent Management live refresh

## What's changed

- **Agent Management / Live** supports **Auto-refresh RPC + SMB (60 sec)**, opt-in. While this tab is visible, the manager refreshes the selected campaign's validated SMB status JSON and separately checks RPC TCP/135 reachability.
- **RPC reachable only** no longer hides computers from future probes: it only filters the visible list. The search box and AD activity filter still constrain requests to the exact selected OU and security group.
- **Fair scans for large OUs:** each read-only pass checks at most 300 distinct AD-verified hostnames, prioritizing never-checked and least-recently-checked entries. The scanner uses at most 12 parallel TCP connections with a 1.35-second per-host timeout.
- **No overlapping refreshes.** Periodic checks stop when leaving Agent Management or closing the manager. If the SMB share cannot be reached, report coverage remains UNKNOWN (not zero).

## Windows GUI clipping fixes

- **Software updates:** the header and subtitle rows now grow with the actual text height; the two-step Check/Install explanation stays below the title at scaled DPI.
- **Campaign Wizard → Continue:** the main navigation button is text-only (no icon stealing horizontal space), keeps a readable primary blue accent, and allocates up to 360 pixels when its caption grows.
- **Connect to Active Directory → Advanced connection options:** the entire checkbox row and optional domain-controller controls get computed heights. The connection page scrolls vertically if the full content exceeds the desktop instead of losing the lowest line.
- The Windows GUI smoke suite asserts these specific controls at 100%, 125%, 150%, and 187.5% font/UI profiles. Interactive 200% DPI verification remains an explicit lab acceptance step.

## Data sources, confidence and security

AD lastLogonTimestamp is replicated and approximate. RPC/135 is only a reachability hint, and is not proof that the Task Scheduler interface is authorized or that an agent is running. The client's `<COMPUTERNAME>.json` file is unauthenticated self-reported diagnostic evidence. Client versions, reported PS1/EXE method, health and UTC timestamps remain separate from independently verified GPO delivery method and remote task-submission outcomes. No automatic refresh creates AD/GPO changes or launches a remote process.

The production agent remains PowerShell. Native EXE deployment remains **laboratory-only**, limited to a strict isolated OU and 1–5 directly listed test computer accounts when explicitly enabled. Full domain GPO/client end-to-end testing has **not** been performed by GitHub-hosted runners.

## Operator notes

See [Agent Management auto-refresh and acceptance guide](AGENT-MANAGEMENT-AUTOREFRESH.md). Existing options remain: Push / Repair / Report / Cleanup, Preview, exact GPO GUID confirmation, Client Health, activity filters and SMB share setup in the campaign Schedule Wizard.

## Validation

Windows x64 and x86 CI should pass compiler, offline feature contracts, PS1/native client tests, updater hash checks and multi-scale WinForms smoke. A live isolated-domain test remains necessary before interpreting report data operationally.
