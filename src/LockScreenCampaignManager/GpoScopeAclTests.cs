using System.Security.AccessControl;
using System.Security.Principal;

namespace LockScreenCampaignManager;

/// <summary>Offline synthetic DACL fixtures; no directory or endpoint access.</summary>
internal static class GpoScopeAclTests
{
    private const int Read = 0x10;
    private const int Apply = 0x100;
    private static readonly SecurityIdentifier Subject = new("S-1-5-21-1-2-3-1001");
    private static readonly SecurityIdentifier Other = new("S-1-5-21-1-2-3-1002");
    private static readonly Guid ApplyRight = new("edacfd8f-ffb3-11d1-b41d-00a0c968f939");
    private static readonly Guid Property = new("11111111-2222-3333-4444-555555555555");

    internal static void Run()
    {
        var grant = Common(AceQualifier.AccessAllowed, Read | Apply);
        Expect("Read+Apply", false, grant);
        Expect("Read", true, grant);
        Expect("Unknown", false, Common(AceQualifier.AccessAllowed, Read));
        Expect("Deny", false, Common(AceQualifier.AccessDenied, Apply), grant);
        Expect("Read", true, Common(AceQualifier.AccessDenied, Apply), grant);
        Expect("Deny", true, Common(AceQualifier.AccessDenied, Read), grant);

        // A conditional grant cannot establish Read+Apply without claims.
        Expect("Unknown", false, Common(AceQualifier.AccessAllowed, Read | Apply, callback: true));
        Expect("Unknown", false, Common(AceQualifier.AccessDenied, Apply, callback: true), grant);
        Expect("Read", true, Common(AceQualifier.AccessDenied, Apply, callback: true), grant);
        Expect("Unknown", true, Common(AceQualifier.AccessDenied, Read, callback: true), grant);
        Expect("Read+Apply", false, Common(AceQualifier.AccessDenied, Read, callback: true, sid: Other), grant);

        // A property-specific deny may block retrieval of a required GPC
        // attribute; the exact property set is not evaluated in this scanner.
        var propertyDeny = Object(AceQualifier.AccessDenied, Read, Property);
        Expect("Unknown", false, propertyDeny, grant);
        Expect("Unknown", true, propertyDeny, grant);
        Expect("Unknown", false, Object(AceQualifier.AccessAllowed, Read, Property),
            Object(AceQualifier.AccessAllowed, Apply, ApplyRight));
        Expect("Read+Apply", false, Common(AceQualifier.AccessAllowed, Read),
            Object(AceQualifier.AccessAllowed, Apply, ApplyRight));
        Expect("Read+Apply", false, Object(AceQualifier.AccessDenied, Apply, Property), grant);
        Expect("Deny", false, Object(AceQualifier.AccessDenied, Apply, ApplyRight), grant);

        // Do not infer a conclusive deny from order-sensitive allow/deny ACEs.
        Expect("Unknown", false, grant, Common(AceQualifier.AccessDenied, Apply));
        Expect("Read+Apply", false, Common(AceQualifier.AccessDenied, Read,
            flags: AceFlags.InheritOnly), grant);
    }

    private static CommonAce Common(AceQualifier qualifier, int mask,
        bool callback = false, SecurityIdentifier? sid = null, AceFlags flags = AceFlags.None) =>
        new(flags, qualifier, mask, sid ?? Subject, callback, callback ? new byte[4] : null);

    private static ObjectAce Object(AceQualifier qualifier, int mask, Guid type) =>
        new(AceFlags.None, qualifier, mask, Subject, ObjectAceFlags.ObjectAceTypePresent,
            type, Guid.Empty, false, null);

    private static void Expect(string expected, bool readOnly, params GenericAce[] entries)
    {
        var acl = new RawAcl(GenericAcl.AclRevisionDS, entries.Length);
        foreach (var ace in entries) acl.InsertAce(acl.Count, ace);
        var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Subject.Value };
        string actual = NativeGpoDomainAudit.ClassifyScopeDacl(acl, sids, readOnly);
        if (actual != expected)
            throw new InvalidOperationException("GPO DACL projection: expected " + expected + ", got " + actual);
    }
}
