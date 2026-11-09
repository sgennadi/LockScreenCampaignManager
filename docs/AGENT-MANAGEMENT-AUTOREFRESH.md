# Agent Management: opt-in live evidence refresh

The **Auto-refresh RPC + SMB (60 sec)** checkbox works only while the Agent Management / Live tab is open. It is off by default. Each enabled refresh reads validated reports for the selected managed GPO campaign from the configured SMB share, then performs bounded read-only RPC TCP/135 checks. A missing or inaccessible share remains **UNKNOWN**, never an assumed zero or a verified offline computer.

### RPC scan fairness and filters

The **RPC reachable only** checkbox affects the displayed computer list but does **not** remove previously unreachable computers from the next RPC scan. Hostname search and the selected Active/Inactive AD filter still constrain the requested hosts to the explicitly selected OU and group. Each request is capped at 300 unique AD-verified endpoints with 12 concurrent connections and a per-host timeout. The least recently tested candidates are checked first, so repeated checks can eventually cover a filtered population larger than 300 without a full-fleet burst.

An RPC TCP/135 result is a network reachability hint only; it cannot establish Task Scheduler permissions, an installed agent, a working scheduled task, or a visible lock-screen image. SMB client reports remain unauthenticated diagnostic evidence. The console never treats either signal as authority to run a command: every Push / Repair / Cleanup / Report action still requires independent AD/GPO/SYSVOL scope validation, a fresh preview and explicit operator confirmation.

Auto-refresh pauses while another scan or SMB read is in progress, stops when the Agent Management tab is not selected and stops when the manager closes. It is **not** a 24/7 central management service. Production native EXE client deployment remains blocked until isolated live domain tests pass.

### Acceptance

Run Windows x86/x64 CI, offline manager-feature contracts and multi-scale GUI smoke. For a live acceptance test in an isolated OU: validate one unreachable-to-reachable transition while the reachable-only view is on; check that changing the activity filter limits the scan; verify that report counters update on a new SMB JSON; confirm that share outage displays UNKNOWN; and verify no remote GPO or task writes occur during an automatic refresh.
