# Client Health source consistency

Editing the SMB root or campaign ID clears visible rows, the previous snapshot and the alert baseline. A revision stamp discards results of earlier asynchronous SMB reads, including edits away and back. CSV and final HTML exports use a single captured snapshot and abort if the source changes while a dialog is open. Existing AD authorization, Repair selection, and CSV escaping are unchanged.

Offline WinForms smoke covers source revisions and direct edits to both fields. Actual Windows domain acceptance remains separate.
