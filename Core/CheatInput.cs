using System.Globalization;
using System.Text;

namespace SafeDialogueTools.Core;

public static class CheatInput
{
    public static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        if (!TryNormalize(text, out var normalized)) return false;
        return int.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseFloat(string? text, out float value)
    {
        value = 0f;
        if (!TryNormalize(text, out var normalized)) return false;
        if (!float.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value))
            return false;
        return float.IsFinite(value);
    }

    private static bool TryNormalize(string? text, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(text)) return false;
        var builder = new StringBuilder(text.Length);
        foreach (var raw in text.Trim())
        {
            var ch = raw;
            if (ch is >= '\uFF10' and <= '\uFF19') ch = (char)('0' + (ch - '\uFF10'));
            else if (ch == '\uFF0B') ch = '+';
            else if (ch is '\uFF0D' or '\u2212') ch = '-';
            else if (ch == '\uFF0E') ch = '.';
            if (char.IsWhiteSpace(ch)) return false;
            builder.Append(ch);
        }
        normalized = builder.ToString();
        return normalized.Length > 0;
    }
}
