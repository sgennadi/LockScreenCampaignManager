# LockScreen Campaign Manager 0.4.0-preview.86 — independent endpoint closure evidence

Preview.86 adds an optional **read-only, two-stage LocalSystem test** for disposable domain-joined Windows 10/11 computers. It is designed for both SYSVOL or dedicated SMB campaign payloads and the existing PS1 / lab-only EXE client variants.

## New independent predeployment / postcleanup acceptance

1. **Baseline**, before creating the GPO: saves a protected local snapshot containing the SHA-256 of the actual Lock Screen policy values, their registry key existence, computer/domain identity and capture UTC. The script never overwrites an existing baseline and refuses already installed or previously completed campaign IDs.
2. **VerifyCleanup**, after GPO unlink, client cleanup, group policy refresh and any required delayed EXE deletion/reboot: independently re-reads actual registry policy, Computer GPO RSoP (exact GUID), local image/agent directory, both scheduled tasks, ownership and final postcleanup agent receipt.
3. Returns `POSTCLEANUP_LOCAL_STATE_MATCHES_BASELINE` **only if all observed conditions agree**. Missing/filtered/ambiguous RSoP, corrupted/missing final receipt, registry mismatch, any remaining task, owner or files produce a fail-closed review status.
4. A small **local-only** Task Scheduler job runs the probe as SYSTEM and is removed after each inspection. Only a protected local baseline and sanitized report are retained; temporary GPRESULT XML is deleted. No AD/GPO/registry/source mutation or data upload is performed.

This closes a dangerous interpretation gap: a `Completed` agent heartbeat is not independently verified cleanup, especially for a native EXE awaiting deletion after reboot. The new tool does **not** itself clean up anything.

### Boundaries

A positive local report is an independent snapshot, **not** proof that images were visually displayed, that GPO cannot reapply in the future, or that the report is remotely tamper-resistant. Hosted GitHub Actions can validate source contracts, Windows PowerShell 5.1 parsing, fail-closed negative execution and ZIP contents; they cannot establish the success of a real-domain test.

Existing SYSVOL/SMB deployment behavior is unchanged. Automatic JPG Photo Screen Saver User GPO remains blocked pending independent Windows 10/11 acceptance. Native EXE agent production deployments remain lab-gated.

[Endpoint Closure Audit procedure](ENDPOINT-CLOSURE-AUDIT.md) · [SMB SYSTEM canary](SMB-SYSTEM-CANARY.md) · [SMB source setup](SMB-CAMPAIGN-SOURCE.md)
