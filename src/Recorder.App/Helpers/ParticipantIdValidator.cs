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

        if (value.Contains(' ', StringComparison.Ordinal))
        {
            reason = "Participant ID must not contain spaces (use a pseudonym, not a name).";
            return false;
        }

        if (value.Contains('@', StringComparison.Ordinal))
        {
            reason = "Participant ID must not look like an e-mail address.";
            return false;
        }

        if (!Pattern.IsMatch(value))
        {
            reason = "Use 1–32 characters: letters, digits, underscore or hyphen; start with a letter or digit.";
            return false;
        }

        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$")]
    private static partial Regex ParticipantPattern();
}
