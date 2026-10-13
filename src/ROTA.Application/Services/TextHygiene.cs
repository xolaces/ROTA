using System.Globalization;
using System.Text;

namespace ROTA.Application.Services;

/// <summary>
/// Removes characters a reader cannot see from player-written text: zero-width spaces and joiners,
/// direction overrides (U+202A–202E, U+2066–2069), soft hyphens, line/paragraph separators and
/// control characters other than newline. They are how a message impersonates another player's
/// name or scrambles the line it lands in. Visible text, including emoji, passes through.
/// </summary>
public static class TextHygiene
{
    public static string StripInvisible(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(rune);
            bool invisible = cat is UnicodeCategory.Format
                                 or UnicodeCategory.LineSeparator
                                 or UnicodeCategory.ParagraphSeparator
                          || (cat == UnicodeCategory.Control && rune.Value != '\n');
            if (!invisible) sb.Append(rune.ToString());
        }
        return sb.ToString();
    }
}
