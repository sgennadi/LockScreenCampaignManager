using System.IO.Compression;
using System.Security.Cryptography;

namespace UniversalUpdater;

/// <summary>
/// Rebind an extracted EXE to the exact hash-verified single-entry archive
/// immediately before installation. The archive is held open without write/delete
/// sharing from digest verification through ZIP validation and entry hashing.
/// This verifies integrity, not the identity of the publisher.
/// </summary>
internal static class SingleExeArchiveBinding
{
    internal static void Verify(string archivePath, string expectedArchiveHash,
        string executablePath, string expectedExecutableHash, UpdateAssetRule rule,
        long maxArchiveBytes, long maxExpandedBytes)
    {
        if (rule.Kind != PackageKind.SingleExeZip ||
            !string.Equals(Path.GetFileName(executablePath), rule.SingleExecutableName, StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(archivePath), rule.AssetName, StringComparison.Ordinal) ||
            !IsSha(expectedArchiveHash) || !IsSha(expectedExecutableHash) ||
            maxArchiveBytes < 1 || maxExpandedBytes < 1)
            throw new InvalidDataException("Invalid single EXE archive verification parameters.");

        using var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (archive.Length < 1 || archive.Length > maxArchiveBytes)
            throw new InvalidDataException("Update archive exceeds its permitted size.");
        RequireHash(Convert.ToHexString(SHA256.HashData(archive)), expectedArchiveHash);
        archive.Position = 0;
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        ZipSafety.ValidateEntries(zip.Entries, rule, maxExpandedBytes);
        var entry = zip.Entries.Single();

        using var executable = new FileStream(executablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (executable.Length != entry.Length || executable.Length > maxExpandedBytes)
            throw new InvalidDataException("Extracted EXE length differs from verified archive entry.");
        RequireHash(Convert.ToHexString(SHA256.HashData(executable)), expectedExecutableHash);

        // Do not rely on the ZIP central directory's claimed uncompressed size.
        using var entryStream = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (read > entry.Length - total || read > maxExpandedBytes - total)
                throw new InvalidDataException("Verified ZIP entry exceeds its declared length.");
            hash.AppendData(buffer, 0, read);
            total += read;
        }
        if (total != entry.Length)
            throw new InvalidDataException("Verified ZIP entry has an unexpected length.");
        RequireHash(Convert.ToHexString(hash.GetHashAndReset()), expectedExecutableHash);
    }

    private static bool IsSha(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void RequireHash(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update EXE does not match the verified archive or digest.");
    }
}
