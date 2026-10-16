using System.Text;

namespace Serial2CNC.Logging;

public static class LogValueSanitizer
{
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var sanitized = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsControl(character) || character is '\u2028' or '\u2029')
            {
                sanitized.Append($"\\u{(int)character:X4}");
            }
            else
            {
                sanitized.Append(character);
            }
        }

        return sanitized.ToString();
    }
}