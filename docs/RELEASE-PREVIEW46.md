# LockScreen Campaign Manager 0.4.0-preview.46

## Native read-only AD and DC verification
- The AD Connection Wizard reads controllers, OUs, security groups, users and GPO inventory using signed and sealed native C# LDAP. No PowerShell process is launched for discovery or the read-only GPO/OU access preflight.
- DC Consistency now performs explicitly scoped, read-only audits through C#, comparing the selected DC's LDAP UTC clock, GPO AD/GPT versions, SYSVOL campaign files and SHA-256 image hashes. Alternate AD account network credentials also apply to SMB checks.
- The legacy AD-Discovery and Dc-Consistency PowerShell engines are no longer embedded in the Manager EXE. Their source stays in the repository temporarily for reference and offline comparison.

## Wizard and updater improvements retained from previous previews
- Clearer Wizard progression from images to order, schedule, save and optional GPO deployment.
- Improved header and step label space, high-DPI-aware title sizing.
- Updater has two steps: Check and Install. Install handles download, verification and manual confirmation.
- Diagnostic GitHub issues open with a prefilled draft for operator review; no report is submitted automatically.

## Scope and validation
- Windows x64/x86 CI builds, static checks, C# self-tests and synthetic UI tests verify the release.
- **Real AD and Windows 10/11 client acceptance tests are not available in hosted GitHub Actions.** Test read-only operations in a controlled domain before depending on production multi-DC results.
- Administrative GPO deployment, lifecycle, conflict scan, immediate actions, computer/group changes and SYSVOL storage still use embedded legacy PowerShell modules. The client-side LockScreen-Agent.ps1 is also unchanged. **This preview does not claim full PS1 removal.**
