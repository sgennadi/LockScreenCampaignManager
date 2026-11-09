# 0.4.0-preview.79 — Complete group GPO conflict report

Group Assignments > **Preview / check conflicts** remains the mandatory, read-only per-target LDAP/SYSVOL preflight. The compact summary shows twelve candidate findings per target for readability, but it never substitutes for the underlying full audit.

## New report

After successful batch Preview click **Full conflict report...** (also available on the last page of the Deployment Wizard). The full report displays all static User- and Computer-Configuration GPO candidate findings (up to 500 per target), including target name, OU/group identity, GPO GUID, risk, exact setting and value, linked OU, source file and coverage notes. Search all fields, choose a risk filter, inspect per-entry details, and export filtered CSV locally after a privacy warning.

Only a current successful Preview enables the report. Changes to connection/DC, OU, security group or campaign settings invalidate its cached contents. The native scanner's total, high-risk and warning counts must match the raw finding array exactly; discrepancies fail the entire Preview rather than producing partial evidence. The full report does not modify AD, SYSVOL or endpoints.

The CSV export handles quotes, newlines and formula-leading characters and rejects network/device paths or reparse-point destinations. It contains internal domain, group and policy details; **review before sharing** and do not upload raw reports to a public GitHub issue.

This is a **static policy-candidate audit, not winning-policy RSoP**. A zero-finding result does not prove there is no conflict. Test real users and computers using gpresult in a private pilot. No endpoint agent changes.

CI exercises complete 25-row CSV snapshots, formula-escaping, source count validation and Windows WinForms layout at multiple simulated font/DPI scales. GitHub-hosted CI cannot validate your production AD.
