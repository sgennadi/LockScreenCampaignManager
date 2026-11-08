# Universal Active Directory deployment

This project is **not tied to a particular AD forest, DNS domain, OU or domain controller**.
The administrator may connect to any reachable writable AD domain controller using
current Windows credentials or another AD account (DOMAIN\\user or user@domain).
The password is only passed to CreateProcessWithLogonW in memory and never written to
configuration files, logs, temporary request JSON or SYSVOL.

## Requirements

- The Windows GUI is a **self-contained .NET 10 single-file EXE** (x64/x86).
- **No RSAT on any machine is required for this deployment path.** The EXE uses Windows built-in
  Windows PowerShell 5.1, signed/sealed LDAP queries, Windows ADSI and the native gpedit.dll COM API.
- **No RSAT, ActiveDirectory/GroupPolicy PowerShell modules or WinRM** on the administrator PC or selected DC.
- Kerberos/Negotiate LDAP to an authorized writable DC (TCP 389), and SMB SYSVOL access (TCP 445).
- Windows must provide the native gpedit.dll COM API, and the account needs GPO creation, ACL, OU-link and SYSVOL rights.
- A domain identity delegated to create/link GPOs and manage SYSVOL content.
- Windows 11 Enterprise/Education on the **target computers**. They do not need RSAT
  or any additional .NET runtime.

## GUI workflow

1. Configure date/time, timezone and images in the campaign editor.
2. Open **AD / GPO Deploy**.
3. Enter a DNS domain, or leave the field empty to attempt auto-detection.
   Optionally select a particular writable DC from those discovered.
4. Choose **Current Windows account**, or **Other domain account** and enter a
   username and password. The connection profile remembers only domain, DC and
   (optionally) user name; it never remembers a password.
5. Click **Connect / Refresh**. Search and select a computer OU and a **security
   group that contains computer accounts** (including possible nested groups).
6. Enter a new GPO name. Click **Preview (no changes)**.
7. Inspect the target domain, DC, OU, security group, file paths and image count.
8. Click **Create GPO + copy to DC**, read the confirmation and explicitly approve.
9. Review the deployment receipt under:
   `%LOCALAPPDATA%\\LockScreenCampaignManager\\Deployments\\`.
10. Verify the GPO in GPMC and test on **one** Windows 11 Enterprise/Education PC
    before linking a production OU/group.

## What Deployment actually does

- Reads domain controllers, OUs, security groups and user lookup through LDAP,
  without local ActiveDirectory or GroupPolicy PowerShell modules.
- Revalidates selected writable DC, OU and security group via Windows ADSI/LDAP on the same DC.
- Verifies all image SHA-256 hashes in a newly built campaign package.
- Fails safely if the GPO name or destination SYSVOL folder already exists.
- Creates `\\<selected-DC>\\SYSVOL\\<dns-domain>\\scripts\\LockScreenCampaigns\\<campaign-id>`.
  It uses a unique staging directory, verifies the uploaded files, then renames it.
- Creates **one new GPO** through the Windows native IGroupPolicyObject COM interface on the administrative computer.
- Changes the new GPO's default Authenticated Users permission to **Read**, not
  **Apply**; grants **Apply** to the selected computer security group only.
- Registers the PowerShell Startup script with the `Machine/Scripts/psscripts.ini`
  file and the GPO's Scripts client-side-extension registration and computer
  version number on the chosen DC. It does not alter existing GPOs.
- Links the newly-created GPO to the selected OU by writing the gPLink LDAP attribute, after all preceding checks pass.
- Records the GUID, OU, group and SYSVOL path in a per-user deployment receipt.

**Selection of a security group never changes who belongs to it.** The group must
already contain the intended computer accounts, with machine accounts in the
selected OU. Computer Startup GPOs cannot be scoped to *users alone* via a
user-only security group.

The GPO only performs installation at computer startup. The campaign's local
SYSTEM scheduled task changes images during the specified days. After the end,
the agent restores original policy values and removes its local task and files.

## Domain cleanup

**The client agent does not delete AD GPOs or SYSVOL sources.** After the campaign,
an AD administrator should unlink/remove the deployment GPO and delete the exact
campaign source directory on SYSVOL, after confirming clients received their
campaign schedule. Windows Event Logs and internal Windows Lock Screen caches are
never purged.

## Security and failure considerations

- Preview does not mutate Active Directory, GPOs or SYSVOL.
- All relevant domain and DC parameters are explicit, avoiding writes to a
  different default domain/PDC by accident.
- AD+SYSVOL are not a transactional datastore. If an error occurs after the
  GPO is created, the deployment tool **reports the new GPO GUID and source path**
  so the administrator can inspect and repair or remove partial work manually.
  It never silently overwrites or deletes an existing GPO.
- Active Directory replication and DFS SYSVOL replication are asynchronous;
  allow time before testing on other DCs.
- A current-domain credential may not have GPO creation rights; the selected
  alternate credential must have appropriate delegated permissions.
- AppLocker/WDAC/forced PowerShell execution policies may block startup scripts.
- Do not deploy an active campaign on computers already controlled by a different
  forced-lock-screen campaign without resolving the policy conflict.
