# LockScreen Campaign Manager — 0.4.0-preview.121

## What's new

1. **Exact AD user SID ↔ interactive HKCU SID correlation.** The read-only selected-DC GPO scope now includes the resolved AD user's `objectSid` independently from group membership status. The optional Windows endpoint HKCU snapshot records the SID of the account **actually running** the capture, not the operator's typed display name.
2. **No user mismatch, no misleading Screen Saver diagnosis.** Pilot Readiness only interprets `ScreenSaveActive`, `ScreenSaveTimeOut`, `SCRNSAVE.EXE`, secure-on-resume and policy-vs-desktop values if the exact selected AD user's SID matches the HKCU SID, the AD scope is complete and current, and the imported snapshot is fresh. A different user, missing selected user or unavailable SID now yields **Not verified**, without attributing another account's HKCU to the target.
3. **Legacy JSON remains readable, but cannot establish identity.** Existing v1 private endpoint reports without `userSid` still import. Their per-user policy findings remain withheld until a new report is captured as the selected user. A nonempty malformed SID or duplicate/unexpected JSON field is rejected. The schema remains v1 with an optional `userSid` field.
4. **Regression and privacy gates.** Native tests cover exact SID, different SID, missing AD selection, legacy snapshot, expired snapshot, malformed SID and no automated remediation. All existing Windows x64/x86, simulated 100–200% UI, offline AD, SYSVOL and updater safety gates remain.

## Operator check

In **GPO Operations → Conflicts → Scan all DC GPOs → Analyze AD scope (no XML)...**, choose the exact **pilot Computer FQDN/OU and pilot AD User (SAM/UPN)**. Rerun the AD scope scan. On the approved Windows client, capture **this PC's user HKCU** as the **signed-in pilot user** (not by using Run as another administrator). Securely transfer the private JSON and select **Import user HKCU JSON...** in the same **Pilot Readiness** window.

Under **User identity correlation**, a fresh exact-SID match may be labeled **Information**, never proof of authenticity. With a missing, stale or mismatched SID, the result must be **Not verified** and must **not** claim the selected user's Screen Saver is disabled or delayed. The downloaded report includes private account/domain/device metadata and must not be attached to public GitHub issues without careful redaction.

## Limits and safety

SID equality is necessary correlation, not cryptographic endpoint authentication. A user-supplied unsigned JSON can be edited; neither equality nor the LDAP read proves the effective client GPO, actual live user session, winning setting, Screen Saver launch or on-screen visual result. Site policies, WMI, MDM, GPP, SYSVOL, loopback and per-user session state remain separately limited. No AD/GPO/SYSVOL/endpoint writes, remote tasks, gpupdate or automatic remediation.
