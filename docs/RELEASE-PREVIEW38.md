# LockScreen Campaign Manager 0.4.0-preview.38

## Wizard and layout corrections
- The Wizard navigation is now pinned directly above the image editor, not inside the bottom tools footer. **Back**, **Next**, and the highlighted action remain visible on smaller screens and high DPI.
- Six explicit steps: **AD sign-in / Skip AD → Import images → Arrange images → Schedule → Validate & Save → GPO deployment / Finish offline**.
- The first step offers an explicit AD connection and read-only inventory. Users without domain deployment rights can choose **Skip AD**, create a campaign and finish offline.
- The final stage offers either **Connect to AD / GPO deployment** or **Finish offline**, without writing AD or GPO in the offline path.
- Upon adding images through the Wizard, it advances to Arrange. After saving a schedule it advances to Validate & Save. A successful project save advances to the optional deployment step; canceled or failed saves do not advance.
- The main window keeps the separate Wizard/Advanced modes, contextual manual menus, and saved dimensions/split ratio.
- The dedicated GPO deployment window reuses session credentials and checks read-only OU/GPO permissions with warnings when effective write access cannot be confirmed.
- UniversalUpdater's operator-approved Install & Restart remains available for verified portable EXEs, including backup and rollback support.

## Windows support and limits
- Windows 10/11 x64 and x86 manager builds.
- Client lock screen policy supports eligible Windows 10/11 Enterprise, Education and IoT Enterprise editions.
- Hosted Windows CI and offline AD tests do not prove actual SYSVOL delegation, GUI fit on every DPI setup, or an on-device EXE update. Pilot those before production rollout.
