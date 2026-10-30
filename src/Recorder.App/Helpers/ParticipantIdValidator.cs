using System.Globalization;
using System.Text.RegularExpressions;

namespace Recorder.App.Helpers;

internal static partial class ParticipantIdValidator
{
    private static readonly Regex Pattern = ParticipantPattern();

    public static bool TryValidate(string? value, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            reason = "Participant ID is required.";
            return false;
        }

        var trimmed = value.Trim();
        if (!string.Equals(trimmed, value, StringComparison.Ordinal))
        {
            reason = "Participant ID must not have leading or trailing spaces.";
            return false;
        }

        if (trimmed.Contains(' ', StringComparison.Ordinal))
        {
            reason = "Participant ID must not contain spaces (use a pseudonym, not a name).";
            return false;
        }

        if (trimmed.Contains('@', StringComparison.Ordinal))
        {
            reason = "Participant ID must not look like an e-mail address.";
            return false;
        }

        if (!Pattern.IsMatch(trimmed))
        {
            reason = "Use 1–32 characters: letters, digits, underscore or hyphen; start with a letter or digit.";
            return false;
        }

        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,31}\\z")]
    private static partial Regex ParticipantPattern();
}

internal static partial class StudyCodeValidator
{
    private static readonly Regex Pattern = StudyPattern();

    public static bool TryValidateOptional(string? value, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        if (!Pattern.IsMatch(trimmed))
        {
            reason = "Study code must be 1–32 characters: letters, digits, underscore or hyphen; start with a letter or digit.";
            return false;
        }

        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,31}\\z")]
    private static partial Regex StudyPattern();
}
