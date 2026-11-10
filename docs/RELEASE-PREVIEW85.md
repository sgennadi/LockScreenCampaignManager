# LockScreen Campaign Manager 0.4.0-preview.85 — SMB startup / RSoP evidence

Preview.85 improves the Preview.84 LocalSystem SMB canary with **computer GPO applicability and post-deployment reboot checks**. Nothing is deployed automatically by the test harness.

- New deployedUtc timestamp in the native deployment receipt, alongside exact GPO GUID, content mode, campaign ID and pinned manifest/agent SHA-256. A new Preview.85 deployment receipt is required for the updated canary; do not guess missing fields.
- The opt-in local-only SYSTEM canary checks that the Windows client's last boot occurred **after** that deployment timestamp and queries `gpresult.exe /scope computer /x` into an admin-protected temporary XML.
- The parser matches only the **exact GPO GUID in ComputerResults GPO Path** and checks Enabled, IsValid, FilterAllowed and AccessDenied. It ignores User GPOs and spoofed/display names, fails closed for missing, filtered, disabled, duplicate or ambiguous RSoP entries and never publishes raw XML.
- The temporary RSoP XML is removed. Private local JSON reports expose only coarse result codes: source hash verification, local cache presence/integrity, reboot-after-deployment observation and Computer RSoP applicability.
- New Windows PowerShell 5.1 fixture tests run in x86/x64 CI and are included in offline lab packaging. The existing SYSVOL and legacy PS1 Lock Screen agent behavior is unchanged.

**Important limits:** RSoP applicability does not prove that a specific registry setting won, that Startup executed during boot, or that the lock screen was displayed. Those require observation on a real disposable Windows 10/11 domain client. The JPG Photo Screen Saver automatic User GPO remains blocked pending separate `.scr` and User RSoP acceptance. The EXE agent remains lab gated.

[Local SYSTEM canary procedure](SMB-SYSTEM-CANARY.md) · [SMB source setup](SMB-CAMPAIGN-SOURCE.md)
