# LockScreen Campaign Manager 0.4.0-preview.36

## Cleaner default Group Policy names
- The deployment wizard now proposes a compact GPO name, for example **LockScreen - 20261109-1400**, instead of the repetitive **LockScreen - lock-screen-campaign-202611091400**.
- The default keeps the campaign start date and time, so names remain easy to recognize and sort in Group Policy Management.
- For a campaign bound to a named company profile, the name retains a short company-specific suffix to reduce GPO name collisions.
- Administrators can edit the proposed policy name before Preview or Deploy.
- **Existing policy names are not renamed**, and the internal campaign ID, SYSVOL folder identity, update, restoration and cleanup paths are unchanged.

This release also includes the previous image-first Wizard, Advanced mode, responsive action menus, Windows 10 Enterprise/Education/LTSC client agent support, and Windows Server Desktop Experience manager compatibility guidance.

Windows x64/x86 portable EXEs are built and self-tested with GitHub Actions. Actual GPO deployment still requires a domain-connected pilot.
