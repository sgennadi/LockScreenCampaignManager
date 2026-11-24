using System.Text;

namespace LockScreenCampaignManager;

/// <summary>Offline AD/SYSVOL precedence and typed Registry.pol fixtures.</summary>
internal static class LoopbackPolicyProjectionTests
{
    private const string Domain = "DC=example,DC=com";
    private const string Parent = "OU=Clients," + Domain;
    private const string Child = "OU=Pilot," + Parent;
    private const string Key = @"Software\Policies\Microsoft\Windows\System";

    internal static void Run()
    {
        var normal = new LoopbackLinkCandidate(Parent, 1, 1, false, "AD candidate", "1");
        var child = new LoopbackLinkCandidate(Child, 2, 2, false, "AD candidate", "2");
        Expect("Replace candidate", true, normal, child);
        Expect("Merge candidate", true, normal with { Enforced = true }, child);
        Expect("Merge candidate", true, normal with { Enforced = true },
            child with { Enforced = true });
        Expect("Replace candidate", true, normal,
            normal with { Order = 2, Enforced = true, Value = "2" });
        Expect("Merge candidate", true, normal,
            normal with { Order = 2, Value = "2" });
        Expect("Unknown", true, normal, normal with { Value = "2" });
        Expect("Unknown", true, normal, child with { State = "Conditional: WMI not evaluated" });
        Expect("Merge candidate", true, normal, child with { State = "Excluded: link disabled" });
        Expect("Unknown", false, normal);
        Expect("Not configured", true);
        Expect("Not configured", true, normal with { Value = "0" });
        Expect("Unknown", true, normal with { Value = "3" });

        CheckPol("2", true, (Key, "UserPolicyMode", 4u, BitConverter.GetBytes(2u)));
        CheckPol("1", true, (Key, "UserPolicyMode", 4u, BitConverter.GetBytes(1u)));
        CheckPol("", false, (Key, "UserPolicyMode", 1u, Encoding.Unicode.GetBytes("2\0")));
        CheckPol("", false, (Key, "UserPolicyMode", 4u, BitConverter.GetBytes(3u)));
        CheckPol("", false, (Key, "UserPolicyMode", 4u, BitConverter.GetBytes(2u)),
            (Key, "UserPolicyMode", 4u, BitConverter.GetBytes(1u)));
        CheckPol("", false, (Key, "**Del.UserPolicyMode", 1u, Encoding.Unicode.GetBytes(" \0")));
        CheckPol("", false, (Key, "**DelVals.", 1u, Encoding.Unicode.GetBytes(" \0")));
        CheckPol("", false, (Key, "**DeleteValues", 1u, Encoding.Unicode.GetBytes("UserPolicyMode\0")));
        CheckPol("", false, (Key, "**soft.UserPolicyMode", 4u, BitConverter.GetBytes(1u)));
        CheckPol("", false, (@"Software\Policies\Microsoft\Windows", "**DeleteKeys", 1u,
            Encoding.Unicode.GetBytes("System\0")));
        CheckPol("", true, (Key, "OtherValue", 4u, BitConverter.GetBytes(2u)));

        if (GpoLinkPriority.Compare(false, 1, Parent, 1, true, 1, Parent, 2) <= 0)
            throw new InvalidOperationException("Enforced link must outrank an ordinary same-OU link.");
    }

    private static void Expect(string expected, bool complete, params LoopbackLinkCandidate[] rows)
    {
        string actual = LoopbackPolicyProjection.Decide(rows, complete);
        if (actual != expected)
            throw new InvalidOperationException("Loopback mode expected " + expected + ", got " + actual);
    }

    private static void CheckPol(string expected, bool reliable,
        params (string Key, string Name, uint Type, byte[] Data)[] entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        writer.Write(0x67655250u);
        writer.Write(1u);
        foreach (var entry in entries)
        {
            writer.Write((ushort)'[');
            writer.Write(Encoding.Unicode.GetBytes(entry.Key + "\0;"));
            writer.Write(Encoding.Unicode.GetBytes(entry.Name + "\0;"));
            writer.Write(entry.Type);
            writer.Write((ushort)';');
            writer.Write((uint)entry.Data.Length);
            writer.Write((ushort)';');
            writer.Write(entry.Data);
            writer.Write((ushort)']');
        }
        writer.Flush();
        string actual = LoopbackPolicyProjection.ReadRegistryPol(stream.ToArray(), out bool valid);
        if (actual != expected || valid != reliable)
            throw new InvalidOperationException("Unexpected Registry.pol loopback projection.");
    }
}
