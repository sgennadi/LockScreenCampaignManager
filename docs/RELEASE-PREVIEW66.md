# LockScreen Campaign Manager 0.4.0-preview.66

## Agent Management / Live

The former **Run now on PCs** tab is now **Agent Management / Live**. It shows the verified computers from the explicitly selected OU and nested AD computer security group. Opening the tab reads this AD scope and probes the first 300 visible hostnames' TCP/135 RPC endpoint-mapper reachability with bounded concurrency and short timeouts; more computers can be inspected by searching/filtering.

- Search by computer name or SAM; optionally show only RPC-reachable computers.
- **Check RPC reachability** scans the currently visible AD computers. The check is read-only and never runs a remote process.
- **Select reachable (max 25)** prepares the existing scoped action path.
- Select a mode from **Push / Repair / Cleanup / Report**, click **Preview**, then **Run on selected** and confirm the managed GPO GUID. Every remote action checks OU/group/GPO scope again and uses Task Scheduler RPC with permitted administrator credentials.
- **Open Client Health** shows the agent's last feedback and installed version. Optional 60-second reachability refresh is disabled by default.
- The last step of the Deployment Wizard now exposes a direct **Agent Management / GPO...** shortcut after OU/group selection.

### Important status semantics

A successful TCP/135 connection is *RPC endpoint-mapper reachability*, **not** proof of logon, Task Scheduler RPC authorization, a running/installed client agent, successful remote execution, or an image actually visible at the lock screen. A failed TCP/135 probe is **not proof that a computer is powered off**: a firewall or DNS can block it. Only a fresh agent Report and a trusted Client Health result improve confidence in agent status. Never call these hints guaranteed online/offline.

### EXE client status

The production main release still uses the PowerShell endpoint client. A native EXE client with a distinct GPO Startup method exists in a separate **lab-only** branch; its production deployment is explicitly disabled until isolated real AD / Windows 10/11 tests are completed. Agent Management does not run arbitrary EXE commands on domain computers.

### Validation

GitHub Windows x86/x64 compiler, offline contracts, internal self-tests and the UI smoke suite run before preview publication. CI does not connect to any real organizational DC, create production GPOs, or test RPC reachability on actual client PCs.
