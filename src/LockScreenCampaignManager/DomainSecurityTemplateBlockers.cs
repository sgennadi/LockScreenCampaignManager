using System.Globalization;
using System.Text;

namespace LockScreenCampaignManager;

/// <summary>
/// Bounded, read-only MS-SCE security-template INF subset reader. Only the
/// exact Computer machine-inactivity registry policy is classified. Other
/// SecEdit data (ACL/SID/rights) is not interpreted or applied.
/// Decoding follows the encoding-evidence pattern from GPOSettingsExplorer.
/// </summary>
internal static class DomainSecurityTemplateBlockers
{
    private const int MaxFile = 2097152;
    private const int MaxLines = 12000;
    private const string MachineKey =
        @"MACHINE\Software\Microsoft\Windows\CurrentVersion\Policies\System\InactivityTimeoutSecs";
    private const string PolicyKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";

    internal static IReadOnlyList<NativeGpoConflictScan.Hit> Parse(byte[] bytes)
    {
        if (bytes is null || bytes.Length is < 1 or > MaxFile)
            throw new InvalidDataException("Invalid GptTmpl.inf source length.");
        string data = Decode(bytes);
        if (data.Contains('\0', StringComparison.Ordinal))
            throw new InvalidDataException("GptTmpl.inf contains invalid embedded NUL.");
        var found = new List<NativeGpoConflictScan.Hit>();
        using var reader = new StringReader(data);
        string? line;
        string section = "";
        int count = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            if (++count > MaxLines || line.Length > 8192)
                throw new InvalidDataException("Security template line limit exceeded.");
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') ||
                trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith('['))
            {
                if (!trimmed.EndsWith(']') || trimmed.Length < 3)
                    throw new InvalidDataException("Invalid GptTmpl.inf section header.");
                section = trimmed[1..^1].Trim();
                continue;
            }
            if (!section.Equals("Registry Values", StringComparison.OrdinalIgnoreCase))
                continue;
            int equals = trimmed.IndexOf('=');
            if (equals <= 0)
                continue;
            string key = trimmed[..equals].Trim();
            if (!key.Equals(MachineKey, StringComparison.OrdinalIgnoreCase))
                continue;
            if (found.Count != 0)
                throw new InvalidDataException("Ambiguous duplicate Machine inactivity policy.");
            string encoded = trimmed[(equals + 1)..].Trim();
            string[] tokens = encoded.Split(',', 2);
            if (tokens.Length != 2 || tokens[0].Trim() != "4")
                throw new InvalidDataException("Machine inactivity INF value is not REG_DWORD.");
            string value = tokens[1].Trim();
            uint seconds;
            bool parsed = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? uint.TryParse(value[2..], NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out seconds)
                : uint.TryParse(value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out seconds);
            if (!parsed)
                throw new InvalidDataException("Invalid machine inactivity timeout in INF.");
            found.Add(new NativeGpoConflictScan.Hit(PolicyKey,
                "InactivityTimeoutSecs", seconds.ToString(CultureInfo.InvariantCulture),
                "Warning", "Security Options machine inactivity lock (GptTmpl.inf). " +
                "Verify endpoint RSoP; a source value is not a winning policy."));
        }
        return found;
    }

    private static string Decode(byte[] bytes)
    {
        try
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return new UnicodeEncoding(false, true, true)
                    .GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return new UnicodeEncoding(true, true, true)
                    .GetString(bytes, 2, bytes.Length - 2);
            var utf8 = new UTF8Encoding(false, true);
            if (bytes.Length >= 3 && bytes[0] == 0xEF &&
                bytes[1] == 0xBB && bytes[2] == 0xBF)
                return utf8.GetString(bytes, 3, bytes.Length - 3);
            int count = Math.Min(bytes.Length - bytes.Length % 2, 200);
            int oddNuls = 0;
            for (int i = 1; i < count; i += 2)
                if (bytes[i] == 0) oddNuls++;
            if (count > 30 && oddNuls > count / 5)
                return new UnicodeEncoding(false, false, true).GetString(bytes);
            return utf8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(
                "GptTmpl.inf encoding is invalid or unknown; not guessed.", ex);
        }
    }
}
