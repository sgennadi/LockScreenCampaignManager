# LockScreen Campaign Manager — 0.4.0-preview.122

## What's new

1. **Local SCR/EXE file check.** The optional logged-in user HKCU probe now checks whether the selected local .scr/.exe can be opened **read-only**, without launching it. Network/UNC paths, non-fixed drives, links and unsupported or unresolved names are not read.
2. **Actual HKCU time vs power.** The unified GPO Pilot Readiness analyzer compares the exact selected AD user's fresh ScreenSaveTimeOut against observed AC/DC display-off, sleep and machine inactivity lock timers. Reports how many seconds earlier a power/lock event may occur.
3. **Source conflicts and incomplete evidence.** Different active-plan WMI and HKLM policy power values are flagged as **Not verified** instead of promoting a false blocker. Old HKCU JSON without local SCR evidence remains importable.
4. **Safety and regression coverage.** Keep exact user SID correlation, strict optional JSON validation, no network/EXE launch/registry write, EN/RU/HE diagnostic labels and Windows x64/x86 + Offline AD CI gates.

## How to verify

On a disposable domain-joined Windows 10/11 pilot computer, run the **Endpoint user Screen Saver probe** as the actual signed-in test user and choose **Capture this Windows user**, then save the private local JSON. In the manager, select **GPO Operations → Conflicts → Pilot Readiness**, choose the exact computer/OU, **Analyze AD scope (no XML)** for the same user SAM/UPN, and **Import user HKCU JSON...**. The **User Screen Saver file access** row distinguishes Readable, Missing, Denied, Linked, NonLocal, Unresolved, Unsupported and Unavailable. For the comparison with power settings, use the approved **Verify power (PC)...** read-only action. Review AC/DC source differences separately.

## Limits

`Readable` means one byte could be opened locally in the probe's Windows user context. It does **not** establish a genuine Screen Saver, trustworthy binary signature, executed application, available behavior after idle, effective GPO, winning precedence or on-screen state. HKCU, power, selected DC source inventories and AD SID are still unsigned or read-only observations. A local user profile can have settings distinct from the final policy. A test with an unknown user, expired evidence or conflicting power sources must not claim a confirmed blocker. This version makes no policy, SYSVOL or client registry modifications and never executes chosen SCR/EXE. Real Windows 10/11 acceptance is still needed before broad deployment.
