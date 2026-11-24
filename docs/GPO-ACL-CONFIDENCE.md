# AD-only GPO ACL confidence

The selected-DC applicability projection uses the exact user/computer SID and nested tokenGroups, GPO links and GPC DACL evidence. It does not perform Windows AccessCheck with the real endpoint logon token.

A matching callback ACE may depend on claims that are not available in this read-only LDAP projection. A matching property-specific Read deny may block access to a required GPC property, and an allow followed by a deny can be order-sensitive. These cases now remain Unknown rather than being combined into a misleading Read+Apply or conclusive Deny. Ordinary Read/Apply grants and independently supported denies retain their existing classification. The computer-context User GPO Read check ignores unrelated Apply-only ACEs, as before.

Native offline tests exercise ordinary grants, explicit denies, conditional grants/denies, property-specific Read, specific Apply rights, unrelated SIDs, inherited-only ACEs and order-sensitive cases. The scanner remains read-only; optional endpoint RSoP and actual Windows behavior remain separate evidence.
