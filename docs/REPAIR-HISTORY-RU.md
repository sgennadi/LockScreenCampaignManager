# Repair History — 0.4.0-preview.19

**Status:** preview implementation; Windows CI checks only. It has not yet been tested against your DC, Windows 11 clients or production gMSA identity. The user interface remains English.

## What has changed

Previously each User/gMSA queue kept the **latest current state** in `queue.json`. Once a Repair was verified or cancelled, the record disappeared, leaving no complete timeline.

Now a separate append-only, monthly JSONL journal records state changes in both queues. Events include:

- `Approved` — initial tracking/approval, with the initial RPC result reflected in `State` and `Reason`.
- `RpcStarted` — a remote attempt was persisted before RPC execution.
- `RpcUncertain` — a previously Attempting RPC became AwaitingTask after an interrupted worker or uncertain result.
- `StateChanged`, `DetailsUpdated`, `NeedsAttention` — new queue state or status.
- `HealthyConfirmed` — a fresh, matching Client Health self-report passed the existing verification rules; the tracking record was removed.
- `Cancelled` — an administrator cancelled tracking; this **does not terminate** a remote task.
- `Superseded`, `TrackingRemoved` — the previous record was replaced or removed without independent evidence of success.

Simple rescheduling of a feedback check does **not** count as another RPC attempt.

## GUI

Open **GPO Operations → Device & Repair Dashboard**, select a managed GPO, choose **Load verified AD devices → Refresh reports**, and select a computer. **Device Details** now includes the most recent 30 durable Repair events (User + gMSA), even if the active queue entry has been removed.

Click **Repair history** for a larger English-only, DPI-aware window showing the latest 500 available events for the selected, verified campaign. Filter by computer, action or source (All/User/gMSA). **Export visible CSV** is a manual, confirmed, local export. The window remembers its size/position using the existing DPI-safe shared module.

The viewer is read-only: it does not perform RPC, grant new approvals, cancel jobs, connect to a different AD scope or upload anything to GitHub.

## Locations and data

- Current user's queue and journal: `%LOCALAPPDATA%\LockScreenCampaignManager\DeferredRepair\queue.json` and `repair-history-YYYY-MM.jsonl`.
- Protected gMSA queue and journal: `%PROGRAMDATA%\LockScreenCampaignManager\GmsaWorker\queue\queue.json` and `repair-history-YYYY-MM.jsonl`.

Both journals stay on the respective worker/administrator computer. They include queue entry ID, computer FQDN, campaign ID, GPO GUID, source, timestamp (UTC), previous/new state, attempt count and sanitized operational explanation. They do **not** record passwords, stored session tokens or raw RPC responses. **Treat the files as sensitive operational data**; keep gMSA journal ACLs protected and do not publish raw exports.

Each monthly journal is limited to 16 MiB. The viewer scans up to 48 files and retains up to 250 recent events per source before applying the verified AD scope (maximum 500 combined). Older files are **not deleted automatically**. You can back up/archive them with appropriate access controls. Archives are not automatically read back by the GUI. Existing pre-preview.19 queue history cannot be reconstructed retroactively.

## Failure semantics and limitations

- Queue updates are still committed atomically and guarded by the existing User mutex or protected gMSA file lock. The history entry is appended **after** the queue replacement, under the same queue lock.
- Queue.json and JSONL are **not one atomic transaction**. A process crash, full disk or permission error may create a missing event. Any journal-write error is reported and prevents proceeding to new RPC execution at that point; the queue remains authoritative. An incomplete/invalid JSONL triggers a visible read error rather than falsely appearing empty.
- Historical entries are operational metadata, **not** cryptographically signed proof that RPC ran or that an image appeared on the physical lock screen.
- No history event by itself authorizes another remote repair. Existing 14-day approval, 20 attempts, AD membership verification, GPO scope and Preview/full GUID safeguards remain in force.
- gMSA journal reads use the same protected configuration and local administrator checks as the read-only operational queue. They do not re-enable a disabled worker.
- Local history belongs to the Windows identity that performed the repairs. Managers on different PCs do **not** magically share user history; the gMSA journal lives on the gMSA worker host.

**Testing:** Windows Actions x86/x64 SelfTests exercise User cancellations, healthy confirmations, gMSA restart recovery, creation of a durable RPC timeline, source/scope isolation and corruption rejection. A real AD/Win11 pilot is deliberately deferred.
