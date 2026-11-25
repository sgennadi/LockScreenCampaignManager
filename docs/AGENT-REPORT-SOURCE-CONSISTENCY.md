# Agent Management report source consistency

The Agent Management view uses the Client Health SMB root and the independently selected managed GPO. Editing the root invalidates every in-flight Agent Management report read and clears all prior source-bound report interpretations, report counts, timestamps and method comparisons. Invalid, ambiguous or unavailable sources and failed reads clear those same fields. Independent RPC observations and task-submission history survive a share edit; managed-GPO changes retain the existing behavior of clearing task history.

The AD inventory verification flag distinguishes unknown counts from a successfully verified zero-computer scope. SMB report counts remain unknown until a successful read. The overview now shows read status/failure reasons visibly, uses EN/RU/HE templates for dynamic counts and re-renders on language changes without re-reading AD or changing selected computer objects.

Offline WinForms smoke covers unknown versus verified zero, language switches, a share change away and back, preserving independent RPC/task observations, and failed-read invalidation. No production directory or endpoint access. The result is diagnostic evidence, not authenticated proof of a running client. The rest of dynamic detail text and real monitor/domain acceptance remain open.
