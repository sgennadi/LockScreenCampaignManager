using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only local selection of a custom PE .scr/.exe for an operator pilot.
/// No EXE-to-SCR conversion, execution, automatic install, domain delivery or
/// claim of /s /p /c compatibility is made. A renaming operation alone cannot
/// establish Windows screensaver command-line support.
/// </summary>
internal static class CustomSaverPilotFile
{
    private static readonly Regex Digest = new(
        "^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal sealed record Observation(string Path, string Sha256,
        long Bytes, string Extension, bool TrustedSignature, string SignatureStatus);

    internal static Observation Inspect(string input) => InspectCore(input, inspectAuthenticode: true);

    // CI must never make trust or revocation network calls.
    internal static Observation InspectPeOnlyForSelfTest(string input) =>
        InspectCore(input, inspectAuthenticode: false);

    private static Observation InspectCore(string input, bool inspectAuthenticode)
    {
        string full = ValidatePath(input);
        FileInfo file = new(full);
        if (!file.Exists || file.Length is < 1024 or > 160L * 1024 * 1024 ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Custom screen saver must be a regular 1 KB–160 MB local PE file.");
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.SequentialScan);
        if (stream.Length != file.Length)
            throw new InvalidDataException("Custom file size changed during inspection.");
        Span<byte> dos = stackalloc byte[64];
        stream.ReadExactly(dos);
        if (dos[0] != (byte)'M' || dos[1] != (byte)'Z')
            throw new InvalidDataException("File is not a Windows PE executable.");
        int peOffset = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
            dos.Slice(0x3c, 4));
        if (peOffset < 64 || peOffset > 4096 || peOffset + 24 > stream.Length)
            throw new InvalidDataException("Invalid Windows PE header offset.");
        stream.Position = peOffset;
        Span<byte> pe = stackalloc byte[24];
        stream.ReadExactly(pe);
        if (pe[0] != (byte)'P' || pe[1] != (byte)'E' || pe[2] != 0 || pe[3] != 0)
            throw new InvalidDataException("Custom screen saver is not a Windows PE executable.");
        stream.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
        var sign = inspectAuthenticode
            ? WindowsAuthenticodeAudit.Inspect(full, cacheOnly: true)
            : new WindowsAuthenticodeAudit.Result(false, "NOT_CHECKED", "PE-only self-test; no signature check.");
        return new Observation(full, hash, file.Length,
            Path.GetExtension(full).ToLowerInvariant(), sign.Trusted,
            sign.Code);
    }

    internal static void ValidateStored(string? path, string? sha256)
    {
        if (string.IsNullOrEmpty(path))
        {
            if (!string.IsNullOrEmpty(sha256))
                throw new CampaignValidationException("Custom Screen Saver hash without file.");
            return;
        }
        try { _ = ValidatePath(path); }
        catch (Exception e) when (e is ArgumentException or InvalidDataException or
            NotSupportedException or PathTooLongException)
        {
            throw new CampaignValidationException(
                "Custom Screen Saver path must be a regular local .scr or .exe.");
        }
        if (sha256 is null || !Digest.IsMatch(sha256))
            throw new CampaignValidationException(
                "Custom Screen Saver requires an inspected SHA-256 digest.");
    }

    private static string ValidatePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 ||
            path.Any(char.IsControl))
            throw new InvalidDataException("Choose a local .scr or .exe file.");
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Use an absolute local Screen Saver filename.");
        string full = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(full) ||
            full.StartsWith(@"\\", StringComparison.Ordinal) ||
            full.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            full.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            Path.GetExtension(full).ToLowerInvariant() is not (".exe" or ".scr"))
            throw new InvalidDataException("Choose a local .scr or .exe file, not UNC/device path.");
        for (DirectoryInfo? folder = new(Path.GetDirectoryName(full)!);
            folder is not null && folder.Parent is not null; folder = folder.Parent)
        {
            if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked folders are not permitted for pilot EXEs.");
        }
        return full;
    }
}
