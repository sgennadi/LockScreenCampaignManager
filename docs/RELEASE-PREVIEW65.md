# LockScreen Campaign Manager 0.4.0-preview.65

## Unified Active Directory account selection

- In the initial AD Connection Wizard and in the later AD/GPO Deployment Wizard, selecting **Use another AD account** opens the same two-page AD user selection flow.
- **Page 1 — Find user.** Begin typing at least one character of the user's name, logon name, UPN or email. A debounced, capped, read-only native LDAP query returns matching users, updating the list as the query changes. Select a result and press Next or double-click.
- **Page 2 — Password.** The selected identity is shown; enter the password and choose Use this account. Back allows selecting a different identity. Changing users clears any previous user's password. Manual entry is available if directory search is not permitted.
- The startup wizard retains the Remember username, Save password securely (Windows Credential Manager), and Forget saved login choices. The Deployment window does not persist its password and removes the previous independent username/password inputs.
- Account search uses current Windows credentials and does not verify the selected identity's password; this only happens during the separately confirmed AD connection. LDAP search never writes AD, GPO or SYSVOL.

## Validation and scope

- Offline .NET WinForms x86/x64 UI layout and security checks test both pages and canceled selections. No GitHub-hosted Windows runner joins the organization's Active Directory.
- GPO Deployment retains all permissions and write confirmations from preview.64. Native C# manager administrative operations remain separate from the PS1 endpoint agent.
- An optional EXE client delivery method is still not shipped; PS1/EXE selection in GPO deployment requires its separate verified implementation.
