# LockScreen Campaign Manager 0.4.0-preview.43

## Advanced AD controller selection
- Fix the Advanced connection checkbox: unchecking and rechecking must not discard the previously discovered domain controllers or selected DC.
- Switching back to Advanced reuses the existing controller list; if none are cached the manager retries DNS SRV and Windows DC Locator discovery. The **Find DCs** button still forces a refresh.
- The explicitly selected DC is only used when Advanced connection options are checked. Leaving Advanced unchecked uses automatic DC discovery for the connection.
- After a successful read-only AD inventory, retain the connected DC in the Advanced selector alongside the discovered controller list.
- Add regression tests for repeated Advanced checkbox toggles and the preserved DC list.

## Guided campaign Wizard
- Fix Wizard progress so importing images and saving a schedule automatically advance to the appropriate subsequent stages, regardless of whether the operator used the guided buttons or main image toolbar.
- Require a successful local project Save before advancing to GPO deployment / Finish offline. Preserve optional read-only AD discovery and local offline editing.

## Validation limits
Hosted Windows x64/x86 builds and offline tests verify compilation and contracts. A real domain-connected Windows 10/11 pilot remains necessary before GPO rollout.
