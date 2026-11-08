# LockScreen Campaign Manager 0.4.0-preview.62

## Campaign Wizard: image preview and clean mode separation

- The **Images** and **Arrange** pages now display a live preview of the selected image inside the same Campaign Wizard window. The file name and path appear beneath the preview; missing or unreadable images show an explanation without closing the wizard.
- Selecting **Wizard** no longer leaves the **Advanced** editor, its image preview, or duplicated inline navigation visible behind the modal wizard.
- Closing the wizard returns to a dedicated Wizard landing screen; **Advanced** appears only after selecting the Advanced mode.
- The image list and preview share the existing in-memory campaign model, so moving, sorting and removing images continue to update the same project.

## AD connection and permissions

- The AD Connection Review performs a **read-only preliminary ACL assessment** after LDAP inventory, including the domain GPO creation container and the default OU link when one is configured.
- Group SIDs are retrieved from the authenticated account's computed tokenGroups attribute when available; incomplete evidence is shown as Unknown.
- **Preliminary ACL results do not prove effective write access** to AD, existing GPOs or SYSVOL. The selected scope and permissions must be revalidated before deployment. No test GPOs or write probes are created by the connection wizard.

## Native C# migration

- Read-only directory discovery, several GPO previews and list operations run natively.
- Link-only GPO lifecycle actions Disable, Enable, Unlink and Relink have a guarded native LDAP Apply path.
- GPO Delete, Reuse, Deployment Apply, remote Push/Repair and SYSVOL Cleanup still retain legacy audited PS1 paths. The client endpoint agent is still PowerShell.
- Unknown legacy invocation modes are blocked.

## Testing and precautions

Windows Actions CI compiles and performs offline x64/x86 checks. Green CI cannot establish that AD ACLs, GPO writes, client display or all DPI environments work in a real domain. Test against an isolated OU and dedicated Windows clients before production deployment. This release does not modify any AD or GPO merely by opening the application.
