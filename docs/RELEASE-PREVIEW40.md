# LockScreen Campaign Manager 0.4.0-preview.40

## Main Wizard readability and connection toolbar

- Replaced the nearly black AD connection bar with a light-blue, readable toolbar. The AD connection status is independently highlighted and text contrast is restored.
- **Wizard** now visibly restarts at the appropriate first step when selected; it is not merely a cosmetic toggle.
- The guided six-stage workflow sits in a taller, two-row panel **above** the image workspace: one full-width step description, then separate Back, Next/Skip AD, and prominent primary action controls.
- The panel has dedicated vertical space, with a scrollable workspace on smaller Windows 10 and Windows 11 displays. Advanced mode reclaims the unused Wizard row.
- Avoid narrow button rows and right-edge clipping by using a dedicated three-column action grid, with larger text-only buttons.
- Keep Windows 10/11 compatibility, per-user window size and position, the image/preview adjustable split and the previously implemented manual updater.
- Update UI regression contracts to check for the new light connection bar and two-row Wizard navigation.

## Tests and limits
Both Windows x86/x64 CI builds and synthetic multi-font-scale WinForms smoke tests should pass before publishing. Real Windows 10/11 desktop visual acceptance at 100–200% DPI and domain GPO delegation still require an interactive pilot.
