using System.Text;

namespace Bassign.Application.Ai;

/// <summary>Task text and names are written by users, so they are DATA, never instructions (spec §3.F.4):
/// strip control characters and the characters we use as delimiters, collapse whitespace, and cap length.</summary>
public static class UntrustedText
{
    public static string Clean(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value) || maxChars <= 0) return string.Empty;
        var sb = new StringBuilder(Math.Min(value.Length, maxChars));
        var lastWasSpace = false;
        foreach (var ch in value)
        {
            var c = ch is '<' or '>' or '`' or '{' or '}' ? ' ' : char.IsControl(ch) ? ' ' : ch;
            if (char.IsWhiteSpace(c))
            {
                if (lastWasSpace) continue;
                c = ' ';
                lastWasSpace = true;
            }
            else
            {
                lastWasSpace = false;
            }
            sb.Append(c);
            if (sb.Length >= maxChars) break;
        }
        return sb.ToString().Trim();
    }
}
