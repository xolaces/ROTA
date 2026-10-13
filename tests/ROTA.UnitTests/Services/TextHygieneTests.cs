using FluentAssertions;
using ROTA.Application.Services;

namespace ROTA.UnitTests.Services;

// Invisible characters are how a chat line impersonates another name or scrambles its layout.
public class TextHygieneTests
{
    [Theory]
    [InlineData("hel\u200Blo", "hello")]            // zero-width space
    [InlineData("\u202Eolleh", "olleh")]             // right-to-left override
    [InlineData("a\u2066b\u2069c", "abc")]           // isolates
    [InlineData("soft\u00ADhyphen", "softhyphen")]   // soft hyphen
    [InlineData("bell\u0007", "bell")]               // control
    [InlineData("line\u2028break", "linebreak")]     // line separator
    public void Strips_what_a_reader_cannot_see(string input, string expected)
        => TextHygiene.StripInvisible(input).Should().Be(expected);

    [Theory]
    [InlineData("gg \U0001F409 well played")]
    [InlineData("Ünïcödé — fine")]
    [InlineData("two\nlines")]
    public void Keeps_visible_text_emoji_and_newlines(string input)
        => TextHygiene.StripInvisible(input).Should().Be(input);

    [Fact]
    public void Null_and_empty_become_empty()
    {
        TextHygiene.StripInvisible(null).Should().BeEmpty();
        TextHygiene.StripInvisible("").Should().BeEmpty();
    }
}
