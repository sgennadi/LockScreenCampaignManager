# LockScreen Campaign Manager 0.4.0-preview.74 — Choose campaign content first

The Campaign Wizard is now **AD Connection → Campaign Type → Images → Arrange → Schedule → Save / Skip → Deploy**. Choose Lock Screen, Screen Saver or Both in the second step, **before selecting any picture**.

## Wizard behavior

- **Lock Screen**: images, ordering, change dates/times; assigns a Computer Configuration GPO to the selected computer OU and computer security group.
- **Screen Saver**: choose a supported built-in Windows `.scr`, idle timeout in seconds and password-on-resume. Skip images, ordering and JPEG schedule entirely; proceed directly to Save and then Deploy. A User Configuration GPO requires a separately verified user OU and user security group. Without those the manager refuses deployment and directs the operator to **Group assignments...**.
- **Both**: configure the Lock Screen image campaign plus Screen Saver. Assign independent computer and user groups per row, with a selectable LockScreen, ScreenSaver or Both mode for each assignment.

The wizard left rail shows skipped JPEG stages for Screen Saver-only. Back from Save goes to Campaign Type, not to an irrelevant image page.

The selection persists in `.project.json`. Older project files with image campaigns remain Lock Screen; older preview.72 group assignments are inferred from their user vs computer targets. A project containing a Screen Saver-only group requires no images. Changing an already configured group matrix to an incompatible top-level type is blocked until you choose Both or edit those assignments.

## Security and compatibility

Screen Saver is per-user HKCU policy; it cannot be automatically targeted by a computer group without an explicitly designed loopback policy. Lock Screen is computer-scoped. This release never silently turns on loopback. The Screen Saver uses built-in Windows `.scr` (not an image slideshow). Existing standalone PS1 agent remains the production Lock Screen method; native EXE batch deployment is still lab-gated.

The separate [MS-GPREG strict serialization fix](https://github.com/sgennadi/LockScreenCampaignManager/pull/14) is merged into main. It validates NUL-terminated Registry.pol and SYSVOL readback before linking the user OU. Real Windows 10/11, GPO security filtering, inheritance and user `gpresult /h` still require an isolated AD pilot.

## Automated verification

Windows x86/x64 builds, native/self-tests, offline AD contracts and six simulated font scales (100, 125, 150, 175, 187.5, 200%) cover the seven-step Wizard and Screen Saver-only zero-image navigation. Hosted GitHub runners do not touch your production AD domain.

See [Screen Saver group targeting](SCREEN-SAVER-GROUP-TARGETING.md).
