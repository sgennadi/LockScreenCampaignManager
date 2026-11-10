# Offline Local Lab Evidence review

1. On an **approved disposable Windows 10/11 domain client**, run the separate [SMB LocalSystem canary](SMB-SYSTEM-CANARY.md) and/or the [two-stage endpoint closure audit](ENDPOINT-CLOSURE-AUDIT.md). Run Baseline before deployment. Follow existing security controls.
2. Keep each output `report.json` **private** on a local disk. Never upload a raw report, baseline, receipt or full RSoP XML into a public bug report.
3. In the Manager open **Tools → Review local lab evidence...**, or open **GPO Operations → Lab evidence**. Choose the corresponding SMB/cleanup `report.json`. The viewer reads local JSON only; it never executes the probe, creates a task, contacts a DC or changes domain settings.
4. Review positive and negative checks separately. A status reported as SUCCESS is **not** a signed attestation: it is not proof that the report came from the selected AD computer, that the lock screen appeared physically, or that no policy can later reapply.
5. Reports older than 24 hours are visibly marked stale. Missing fields, invalid schema/status, unexpected private findings, oversized files, reparse-link inputs and contradictory success flags are rejected; no remote operation is retried.

A local import is **not** an authenticated health heartbeat and must not authorize agent commands. Share-level effective machine-account access and Windows startup timing require real endpoint checks; issue #30 stays open until independently accepted.
