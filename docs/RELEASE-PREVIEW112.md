# LockScreen Campaign Manager — 0.4.0-preview.112

## What's new

1. **Named GPO blockers:** In **Pilot readiness...**, use **Review GPO blockers...** to inspect potential blocking GPOs individually instead of only summary counts.
2. **Full source details:** Show GPO name, GUID, Computer/User scope, setting, configured value, registry key, source file, risk and imported gpresult state.
3. **Evidence confidence:** Prioritize reported-applied GPOs cautiously, separately show filtered, unverified and stale DC/XML evidence; **NOT winning RSoP** or proof of actual blocking.
4. **Search and export:** Filter by scope and review priority, search technical details and use explicit local-only private CSV export with spreadsheet formula protection; EN/RU/HE labels and high-DPI layout.
5. **Release checks:** Add C# regression tests, offline source/read-only/privacy contracts, simulated 100–200% layout checks and Windows x64/x86/Offline AD Lab CI gates.

**Limitations:** This displays recognized source candidates, NOT winning GPO settings. GPO reported-applied flags from unsigned manual XML do not prove a specific value won, security/WMI/GPP filter evaluation, current HKCU or real Screen Saver/Lock Screen behavior. No automatic fixes, changes to domain/client, remote execution or report uploads. A domain-connected Windows 10/11 pilot remains required.

[How to review gpresult](RSOP-EVIDENCE-IMPORT.md) · [Changelog](../CHANGELOG.md)
