# LockScreen Campaign Manager 0.4.0-preview.75 — Explicit Campaign Type selection

This release corrects the Campaign Wizard behavior reported in preview.74.

## Campaign Type and step selection

- A newly created campaign starts with an **empty type selector**. Nothing is selected automatically; **Next is disabled** until the operator chooses **Lock Screen**, **Screen Saver** or **Both**.
- The left wizard navigation shows **Images (skip)**, **Arrange (skip)** and **Schedule (skip)** **only when Screen Saver-only is selected**. The indicators update immediately when switching types; Lock Screen and Both never skip the image stages.
- A new untitled campaign receives a suggested name based on the chosen mode: **Lock Screen Campaign**, **Screen Saver Campaign**, or **Lock Screen and Screen Saver Campaign**. Manually typed project names are not replaced.
- New campaigns may remain untitled until the Wizard mode is chosen, including those created from Advanced. Validation stops new drafts with no selected campaign type. Saved older projects still infer legacy types without changing their names.
- The main editor and Wizard landing now describe the correct **seven-step** workflow, including Campaign Type.

## Validation and safety

The Windows x64/x86 CI jobs run the portable manager build, native self-tests, 100–200% simulated WinForms font/DPI layout checks and offline AD/Registry.pol regression contracts. Tests cover switching between all three campaign modes and a Screen Saver-only campaign proceeding to Save without JPEGs. These tests do not prove real monitor 200% DPI geometry or effective user GPO application.

**No GPO or AD changes are made by release verification.** Screen Saver uses a supported built-in Windows `.scr`; it is **not** a slideshow of campaign JPEGs. The native EXE startup client remains lab-gated and the PowerShell client remains production default. Test User Configuration GPO inheritance, security filtering and `gpresult /h` on an isolated AD pilot before deploying Screen Saver to production.

See [Target modes](SCREEN-SAVER-GROUP-TARGETING.md) and [Preview.74](RELEASE-PREVIEW74.md).
