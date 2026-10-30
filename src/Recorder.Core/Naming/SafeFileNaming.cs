using System.Globalization;

namespace Recorder.Core.Naming;

public static class SafeFileNaming
{
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public const int MaxNameLength = 64;

    public static string SanitizeFileName(string input, int maxLength = MaxNameLength)
    {
        ArgumentException.ThrowIfNullOrEmpty(input);
        Span<char> buffer = stackalloc char[input.Length];
        var length = 0;
        var previousDash = false;
        foreach (var ch in input)
        {
            var ok = ch is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '.' or '_' or '-';
            var normalized = ok ? ch : '-';
            if (normalized == '-')
            {
                if (previousDash)
                {
                    continue;
                }

                previousDash = true;
            }
            else
            {
                previousDash = false;
            }

            if (length < buffer.Length)
            {
                buffer[length++] = normalized;
            }
        }

        var candidate = new string(buffer[..length]).Trim('-', '.');
        if (candidate.Length == 0)
        {
            candidate = "unnamed";
        }

        if (candidate.Length > maxLength)
        {
            candidate = candidate[..maxLength].TrimEnd('-', '.');
        }

        if (WindowsReservedNames.Contains(candidate) || WindowsReservedNames.Contains(Path.GetFileNameWithoutExtension(candidate)))
        {
            candidate = $"_{candidate}";
        }

        return candidate;
    }

    public static string BuildSessionCode(DateTimeOffset recordingStartLocal, Guid sessionId)
    {
        var stamp = recordingStartLocal.LocalDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var suffix = SessionCodeSuffix(sessionId, 0);
        return $"IR-{stamp}-{suffix}";
    }

    public static string BuildSessionCodeWithCollision(DateTimeOffset recordingStartLocal, Guid sessionId, Func<string, bool> folderExists)
    {
        var stamp = recordingStartLocal.LocalDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        for (var offset = 0; offset < 16; offset++)
        {
            var suffix = SessionCodeSuffix(sessionId, offset);
            var code = $"IR-{stamp}-{suffix}";
            if (!folderExists(code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique session code.");
    }

    private static string SessionCodeSuffix(Guid sessionId, int hexOffset)
    {
        var bytes = sessionId.ToByteArray();
        var hex = Convert.ToHexString(bytes).AsSpan();
        var start = hexOffset * 4;
        if (start + 4 > hex.Length)
        {
            start = 0;
        }

        return hex.Slice(start, 4).ToString();
    }
}
