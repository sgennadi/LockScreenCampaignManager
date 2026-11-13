# LockScreen Campaign Manager — 0.4.0-preview.107

## What's new

1. **Verify Screen Saver power (PC):** New explicit read-only button under GPO Operations → Conflicts. Enter one Computer FQDN in the selected AD Computer OU and compare observed Windows power settings to the configured Screen Saver idle time.
2. **Real client readings:** Read the active Windows power-plan **AC/DC display-off and sleep timers** via WMI associations and **HKLM power-policy/inactivity values** via Remote Registry. Show each source separately; zero means *Never* for power timeouts.
3. **GPO side-by-side:** Read scoped linked Computer GPO power candidates and show the source GPO, setting, link and comparison. Highlight values shorter than Screen Saver, equal thresholds, missing data and differences between the active plan and HKLM policies.
4. **No false confirmations:** Every endpoint request revalidates the exact computer account in the selected OU through signed/sealed LDAP and the same credentials. Unknown WMI, Registry, GPO source or current User HKCU is labeled **Not verified**, not zero or proven safe. No remote scripts, process execution, gpupdate, power-plan changes or AD/GPO modifications.
5. **UX / regression:** New wide, scrollable Windows 100–200% DPI dialog, RU/HE UI labels, parser/source/privacy tests, x64/x86 single-file builds, and the existing independent offline Windows AD lab.

**Limitations:** Power-plan WMI and remote HKLM are observations, not winning Group Policy/RSoP, live display measurements, active AC/DC supply detection or proof of the logged-on user's Screen Saver. The configured Screen Saver idle time is from the campaign, not verified User HKCU. MDM/Intune, modern standby, display-driver conditions, GPP item-level targeting, user/device membership and actual resultant power can differ. An unavailable remote read cannot be interpreted as a clean result.

For acceptance, select a domain-connected Windows 10/11 test computer, compare **user AND computer gpresult**, powercfg /query, and actual Screen Saver/monitor behavior during AC and DC idle, including the real user account. No acceptance is claimed from hosted CI.

[Targeting & power GPO guide](OU-AND-SECURITY-GROUP-TARGETING.md) · [Changelog](../CHANGELOG.md)
