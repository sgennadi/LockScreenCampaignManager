# LockScreen Campaign Manager - 0.4.0-preview.113

## What's new

1. Modern, scrollable warnings and confirmations in the updater and application.
2. Safe Yes/No/Cancel handling with No as default for update installation; Esc/X cannot approve.
3. New Screen Saver custom .scr / .exe Browse in campaign wizard for local pilot; inspects PE, SHA-256 and Authenticode without launching.
4. Existing JPG photo slideshow can create PhotoScreenSaver.scr on Save for local pilot.
5. RU/HE labels, high-DPI checks, Windows x64/x86 and Offline AD CI regression gates.

**Safety:** Custom/JPG saver automatic User GPO deployment remains blocked. Ordinary EXE is not automatically a compliant screensaver. Native Windows security prompts are unchanged.
