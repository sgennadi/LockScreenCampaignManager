# LockScreen Campaign Manager — 0.4.0-preview.102

## What's new

1. **Native visual test:** Open **Tools → Visual DPI / language check** (or Help / tools) to review the real Manager at Windows 200% display scaling, without running a separate PowerShell script.
2. **Actual window DPI:** The dialog measures native Windows `GetDpiForWindow` DPI and checks that it runs in a local console. An RDP session or simulated 200% font is not accepted as physical-screen evidence.
3. **EN / Русский / עברית:** Six operator checklist items cover Wizard, Browse, Client Health, Hebrew RTL, mixed technical LTR text and language switching.
4. **Private local results:** Optional report saving requires a successful offline UI smoke, 192 DPI and all six user confirmations. Reports are unsigned and stored only in LocalAppData; no screenshots, private AD identifiers, network upload or GPO changes.
5. **Regression coverage:** Add English/Russian/Hebrew reversible strings, 100–200% simulated WinForms checks, DPI gating unit tests and offline no-network/no-domain-action CI contracts; include the operator guide in the lab kit.

**Acceptance limits:** A completed local checklist is unauthenticated operator observation, not proof that text visually fits or that policy applied. The real physical Windows DPI/RTL check (#36) remains open pending independent inspection. SMB LocalSystem endpoint (#30) and JPG Screen Saver User GPO deployment (#25/#26) remain gated by separate real-client/real-AD tests.

[Visual DPI checklist instructions](../WINDOWS-200-DPI-VISUAL-CHECK.md) · [Changelog](../CHANGELOG.md)
