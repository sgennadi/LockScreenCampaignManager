# LockScreen Campaign Manager — 0.4.0-preview.108

## What's new

1. **Scan all DC GPOs:** New read-only scan in GPO Operations → Conflicts, covering every domain GPO object through the selected, verified DC rather than only one OU. OU selection is optional for inventory; without it, applicability is explicitly not verified.
2. **Potential blockers:** Inspect recognized User Screen Saver, Computer Lock Screen, display-off AC/DC, sleep, machine inactivity (including GptTmpl.inf), GPP power settings and User Policy loopback. Compare Screen Saver-related delays with the campaign timeout.
3. **Relevance and safety:** Distinguish linked candidates, GPOs outside selected OUs, disabled/blocked links and missing User OU context. Show source-file failures and inconsistent GPC/SYSVOL evidence as **not verified**, never as a clean result.
4. **Report and languages:** Searchable, filterable GPO/setting/value/source/link report, RU/HE display labels, high-DPI layout and opt-in protected local CSV export with formula injection safeguards.
5. **Build and tests:** Add bound checks, source parser, read-only scope, CSV evidence and offline WinForms tests to both Windows build and lab pipelines. Windows x86/x64 single-file EXE and existing updater packages remain.

**Important limitations:** All GPO objects are inventoried, but the scanner recognizes only a defined subset of possible blocking settings. The result is NOT a winning RSoP, full arbitrary-GPO/CSE settings inventory or evidence of actual client application. Confirm site/loopback, security and WMI/GPP item-level filters, user HKCU, MDM and both AC/DC power behavior with gpresult and powercfg on a real Windows 10/11 pilot. No AD, SYSVOL, PC, power or GPO changes are made by the scan.

[Scope guide](OU-AND-SECURITY-GROUP-TARGETING.md) · [Changelog](../CHANGELOG.md)
