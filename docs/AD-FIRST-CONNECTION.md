# Step 1 — Connect to DC & Read Active Directory (preview.28)

The first interactive window of LockScreen Campaign Manager now follows the connection-first workflow of BitKeyBridge.

**Before editing or deploying a campaign**, the manager offers an explicit read-only Active Directory connection: current Windows account, or a different domain account whose password stays in the running process and is not saved. Enter a DNS domain and optional DC; an empty domain is detected from the domain-joined computer, while an empty DC lets LDAP discover a controller. A standalone PC can use an explicitly entered domain/DC.

**Connect & Read AD** performs one signed and sealed LDAP session against the chosen DC via the existing embedded PowerShell 5.1 directory-discovery runner, without installing RSAT or requiring WinRM. It verifies RootDSE and domain identity and reads bounded inventories for:
- Domain controllers, including the writable/read-only distinction;
- Organizational units (for computer GPO links);
- Computer security groups;
- Existing GPO names (LDAP `groupPolicyContainer` metadata).

The displayed result includes the chosen DC, domain, observation time, DC writability, OU/group/GPO counts, and a sample of each list. If any list reaches its 5,000-entry safety limit, the UI marks inventory **PARTIAL**, not complete.

**2 Continue to Campaigns** is enabled only after successful parsing of a fresh, same-domain, read-only result. **Work offline (no AD)** is explicitly available, but does not authorize GPO deployment. The main window shows a read-only connection badge and a prominent **1 Connect to DC & Read AD** action to refresh.

On switching company profiles, the current directory snapshot is discarded to prevent mixing domains. **3 GPO Preview / Deploy** requires a successful Step 1 read before opening the original deployment wizard; it also requires at least one image before building an actual campaign. The deployment wizard pre-fills the verified domain/DC but **reconnects and re-reads OU and computer security groups** before its own read-only preview and separate explicit GPO-create approval. This startup read never writes to AD, SYSVOL, registry, GPO or client computers.

The visual language follows BitKeyBridge's existing NI-inspired WinForms tokens: navy headings, blue actions, green for successful status, pale blue/green surfaces, measured labels, and Windows High Contrast/large-font support. No passwords are stored in company profiles, view state or diagnostics.

## Tests and limitations

Windows CI checks C# snapshot parsing (read-only attestation, domain isolation, completeness flag, timestamp), existing campaign input validation, WinForms/DPI 100%/125%/150% smoke tests, and static signed-LDAP/no-domain-write contracts.

**Real domain acceptance remains outstanding:** GitHub-hosted Windows runners do not have access to your domain controllers or Windows 11 clients. Confirm connectivity, actual domain permissions, machine group resolution and DC inventory in a local pilot before production deployment. Live AD access is never claimed by CI.

Previous release `0.4.0-preview.27` remains unchanged until a separately accepted preview.28 release is published.
