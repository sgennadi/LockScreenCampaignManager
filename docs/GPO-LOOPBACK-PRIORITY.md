# AD-only loopback priority

The read-only selected-DC projection now compares eligible Computer Configuration UserPolicyMode settings by the same domain/OU precedence helper as the static winning-policy view. Enforced links precede ordinary links, ancestor Enforced links precede descendant Enforced links, ordinary child OU links precede ancestors, and link order 1 has precedence within the same container and link class. Conflicting equally ranked modes or unresolved candidate scope remain Unknown.

The Registry.pol reader now also exposes validated typed entries to the loopback projection. Only one REG_DWORD UserPolicyMode value in the range 0..2 is accepted from a GPO. Relevant delete/soft instructions, duplicate assignments and unsupported types are unresolved. A malformed AD versionNumber or missing advertised Registry.pol does not mean no loopback. An orphan setting without Registry CSE advertisement is unresolved.

This is an OU/domain estimate, not an actual endpoint RSoP. Site membership, local/MDM, Preferences, scripts, replication and CSE execution remain outside this calculation. No AD or endpoint writes. The existing optional XML correlation is unchanged.

References:
- https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/manage/group-policy/group-policy-processing
- https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpol/5c7ecdad-469f-4b30-94b3-450b7fff868f
- https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpreg/57226664-ce00-4487-994e-a6b3820f3e49
