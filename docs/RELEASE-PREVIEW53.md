# LockScreen Campaign Manager 0.4.0-preview.53

## Full six-page Campaign Wizard and fixed DPI headings
- The **Wizard** button and startup **Start Wizard** open one complete six-page guided flow: AD Connection (or offline), Images (multiple files or folder), Arrange, Schedule, Save and Deploy (or finish offline).
- The AD connection choice opens the existing AD Connection Wizard with read-only LDAP discovery. Back/Next/Finish are handled inside the guided window instead of bouncing between unrelated forms.
- The main LockScreen header, AD Connection Wizard, and Campaign Wizard grow their parent rows using actual text/font metrics, including step subtitle descenders. Scrollable content is preserved at larger DPI/font scales.
- CI now opens each of the six wizard pages and checks the text layout under 100%, 125%, 150% and a simulated larger-font profile. Real interactive testing at Windows 150-200% DPI remains necessary.

## Update available indicator
- When the app opens, a non-blocking **read-only GitHub Releases check** looks for newer compatible Preview or Stable versions. Subsequent checks are throttled to once every six hours on window activation.
- Only verified compatible release metadata creates a warm amber **Update available: VERSION** indicator in the main editor; the campaign wizard also displays a compact update button when applicable.
- Hover the indicator to see installed/available versions, release date and concise release notes. Click to open Updater, which refreshes the metadata with the existing **1 Check / 2 Install** interface.
- Failed/timeout checks remain unknown, not "up to date". The notification never downloads, installs, elevates or replaces files automatically. Explicit confirmation and SHA-256 verification stay in the updater.

## Administrative engine
- GPO Lifecycle read-only List and Preview stay native C#; legacy Apply still rechecks all state and its plan hash before writing. The actual GPO Deployment, Immediate Actions, SYSVOL Cleanup and extended effective-policy verification still use their guarded PowerShell engines.
- The Windows client LockScreen-Agent.ps1 is not modified in this release.

## Testing
- GitHub Actions validates hosted x64/x86 portable EXE, offline security checks, all wizard layouts and the non-network update-indicator fixture.
- Hosted runner tests do not replace live AD, GPO, DFSR or interactive Windows DPI validation.
