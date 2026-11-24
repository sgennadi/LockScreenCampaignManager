# Portable update integrity and restart safety

The portable updater verifies the archive digest from the selected GitHub release, validates its exact single-EXE ZIP manifest, and hashes the extracted EXE. The manager and its existing-version helper now also independently compare that EXE's digest against the contents of the verified ZIP immediately before replacement. The shared manual transaction uses the same guard. An independently changed EXE and recalculated EXE digest cannot satisfy an unchanged verified ZIP.

The helper verifies the installed bytes before the new EXE self-test, then again after it. An earlier backup is retained until the new process starts successfully. A failure to remove the old checkpoint after restart is logged rather than triggering rollback of a running executable. On subsequent updates, an unexpected remaining checkpoint still requires operator attention.

Offline synthetic regression cases run in both full and updater-specific native self-tests: matching package, changed EXE with either old or recalculated digest, wrong archive digest, archive and expansion limits, and unexpected ZIP entries.

This change does not authenticate a publisher, add signing, or enable unattended installation. Explicit approval, architecture checks, existing rollback behavior, and the separate gMSA maintenance procedure remain required. Real Windows restart/rollback acceptance is still separate from hosted build/self-test results.
