using Monitoring.Ui;
using Shouldly;
using Xunit;

namespace Monitoring.Ui.Tests;

public class PageTests
{
    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("app.js", "text/javascript; charset=utf-8")]
    [InlineData("styles.css", "text/css; charset=utf-8")]
    public void Every_file_is_embedded_and_has_its_media_type(string path, string mediaType)
    {
        // Arrange: the path comes from the test data

        // Act
        var text = UiAssets.ReadText(path);
        var type = UiAssets.ContentTypeOf(path);

        // Assert
        text.ShouldNotBeNullOrWhiteSpace();
        type.ShouldBe(mediaType);
    }

    [Theory]
    [InlineData("missing.html")]
    [InlineData("../app.js")]
    [InlineData("")]
    public void A_file_that_is_not_part_of_the_page_cannot_be_opened(string path)
    {
        // Arrange: the path comes from the test data

        // Act
        var stream = UiAssets.Open(path);

        // Assert
        stream.ShouldBeNull();
    }

    [Fact]
    public void The_page_declares_its_language_and_a_title()
    {
        // Arrange
        var html = UiAssets.ReadText("index.html")!;

        // Act
        var hasLanguage = html.Contains("<html lang=\"en\">", StringComparison.Ordinal);
        var hasTitle = html.Contains("<title>Monitoring</title>", StringComparison.Ordinal);

        // Assert
        hasLanguage.ShouldBeTrue();
        hasTitle.ShouldBeTrue();
    }

    [Fact]
    public void The_page_loads_only_its_own_script_and_style_sheet()
    {
        // Arrange
        var html = UiAssets.ReadText("index.html")!;

        // Act
        var scripts = System.Text.RegularExpressions.Regex.Matches(html, "<script[^>]*>").Select(m => m.Value).ToList();
        var inlineStyles = html.Contains("style=\"", StringComparison.Ordinal) || html.Contains("<style", StringComparison.Ordinal);

        // Assert: nothing inline, so a content security policy without 'unsafe-inline' is enough
        scripts.ShouldBe(["<script src=\"/app.js\" defer>"]);
        inlineStyles.ShouldBeFalse();
    }

    [Theory]
    [InlineData("innerHTML")]
    [InlineData("outerHTML")]
    [InlineData("insertAdjacentHTML")]
    [InlineData("document.write")]
    [InlineData("eval(")]
    public void The_script_never_puts_data_into_the_page_as_markup(string forbidden)
    {
        // Arrange: names and labels come from agents and must only ever be shown as text
        var script = UiAssets.ReadText("app.js")!;

        // Act
        var uses = script.Contains(forbidden, StringComparison.Ordinal);

        // Assert
        uses.ShouldBeFalse();
    }

    [Fact]
    public void The_script_listens_to_the_live_stream_and_to_its_keep_alive()
    {
        // Arrange
        var script = UiAssets.ReadText("app.js")!;

        // Act
        var listens = script.Contains("new EventSource(\"/api/events\")", StringComparison.Ordinal)
            && script.Contains("addEventListener(\"keep-alive\"", StringComparison.Ordinal);

        // Assert
        listens.ShouldBeTrue();
    }
}
