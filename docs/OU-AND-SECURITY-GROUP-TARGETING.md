# OU and security group targeting — Preview.100

**A security group is a filter, not a GPO link.** Group Policy must be linked to an OU (or domain/site). A security group can reside elsewhere in Active Directory; its members can be within the linked OU.

## In the AD Deployment Wizard

1. **Select an OU → Groups with computers in selected OU (direct).** The app reads computer accounts in the selected OU and descendant OUs with signed/sealed LDAP and shows only security groups listed in those computers' direct memberOf memberships. The OU of the group object does not matter.
2. **Choose a group first → All domain security groups.** Select the security group, then select the explicit OU to LINK the GPO. Choosing a group alone never authorizes deployment. You can switch to All groups at any time to include nested/primary groups not listed by the direct view.
3. **Same short OU names** display their parent OU paths; exact duplicate DNs are removed, but the full DN remains the actual identity.
4. **Screen Saver → User OU / groups...** uses User Configuration (HKCU) with a USER OU/group. The group matrix has an optional direct-user-membership filter that can be turned off to browse all groups. Lock Screen uses Computer Configuration (HKLM) and COMPUTER groups. Both requires two independent target pairs.
5. **Browse...** for SMB content is in a separate full-width folder row, visible without scrolling horizontally. It is enabled only after selecting SMB network folder (UNC).

## Limits and safeguards

Direct memberOf queries DO NOT expand nested groups or include primary groups. An OU lookup over 5,000 objects or a failed LDAP query does not produce a misleading partial result: the UI switches back to All groups with a warning. OU and group filtering is for browsing only, not proof that a GPO applies. Read-only Preview and a real Windows gpresult/RSoP check must account for nested membership, denies, WMI, inheritance and loopback.

For Screen Saver applied to users on particular computers, Group Policy loopback requires explicit separate design and approval; the app does not enable it automatically. Automatic JPG Photo Screen Saver User GPO deployment remains blocked pending real Windows 10/11 AD acceptance (#25/#26).

No domain change is made by searching/filtering. The existing separate Preview/Deploy confirmation, GPO scope verification, identity checks, rollback requirements and safeguards are unchanged. Physical DPI 200% and real domain filter acceptance require a test workstation and are not proven by hosted GitHub Actions.
