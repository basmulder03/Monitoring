using Monitoring.Ui;
using Shouldly;
using Xunit;

namespace Monitoring.Ui.Tests;

public class ContrastTests
{
    private const string Light = ":root";
    private const string Dark = ":root[data-theme=dark]";

    private static readonly string[] TextTokens = ["fg", "muted", "healthy", "degraded", "unhealthy", "unknown"];

    public static TheoryData<string, string, string> TextOnBackgrounds()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var theme in new[] { Light, Dark })
        {
            foreach (var text in TextTokens)
            {
                foreach (var background in new[] { "bg", "surface" })
                {
                    data.Add(theme, text, background);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TextOnBackgrounds))]
    public void Text_has_a_contrast_of_at_least_4_5_to_1_on_the_surfaces_it_sits_on(string theme, string text, string background)
    {
        // Arrange
        var tokens = CssTokens.Read(UiAssets.ReadText("styles.css")!, theme);

        // Act
        var contrast = Wcag.Contrast(tokens[text], tokens[background]);

        // Assert
        contrast.ShouldBeGreaterThanOrEqualTo(4.5, $"{text} on {background} in {theme} is {contrast:0.00}:1");
    }

    [Theory]
    [InlineData(":root", "border", "bg")]
    [InlineData(":root", "border", "surface")]
    [InlineData(":root", "focus", "bg")]
    [InlineData(":root[data-theme=dark]", "border", "bg")]
    [InlineData(":root[data-theme=dark]", "border", "surface")]
    [InlineData(":root[data-theme=dark]", "focus", "bg")]
    public void Borders_and_focus_rings_have_a_contrast_of_at_least_3_to_1(string theme, string part, string background)
    {
        // Arrange
        var tokens = CssTokens.Read(UiAssets.ReadText("styles.css")!, theme);

        // Act
        var contrast = Wcag.Contrast(tokens[part], tokens[background]);

        // Assert
        contrast.ShouldBeGreaterThanOrEqualTo(3.0, $"{part} on {background} in {theme} is {contrast:0.00}:1");
    }

    [Fact]
    public void The_dark_theme_follows_the_system_with_the_same_colours_as_the_forced_dark_theme()
    {
        // Arrange
        var css = UiAssets.ReadText("styles.css")!;
        var forced = CssTokens.Read(css, Dark);

        // Act
        var followsSystem = CssTokens.Read(css, "  :root:not([data-theme=light])");

        // Assert
        followsSystem.ShouldBe(forced);
    }

    [Fact]
    public void Both_themes_define_every_token()
    {
        // Arrange
        var css = UiAssets.ReadText("styles.css")!;

        // Act
        var light = CssTokens.Read(css, Light).Keys;
        var dark = CssTokens.Read(css, Dark).Keys;

        // Assert
        dark.ShouldBe(light, ignoreOrder: true);
    }
}
