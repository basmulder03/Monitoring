using Microsoft.Extensions.Configuration;
using Monitoring.Hosting;
using Shouldly;
using Xunit;

namespace Monitoring.Hosting.Tests;

public class SettingsReaderTests
{
    [Fact]
    public void Required_returns_the_trimmed_value()
    {
        // Arrange
        var reader = new ReaderBuilder().With("Agent:ScopeName", "  shop ").Build().Section("Agent");

        // Act
        var value = reader.Required("ScopeName");

        // Assert
        value.ShouldBe("shop");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Required_names_the_full_setting_when_it_is_missing_or_blank(string? value)
    {
        // Arrange
        var reader = new ReaderBuilder().With("Agent:ScopeName", value).Build().Section("Agent");

        // Act
        var read = () => reader.Required("ScopeName");

        // Assert
        read.ShouldThrow<ConfigurationException>().Message.ShouldContain("'Agent:ScopeName'");
    }

    [Theory]
    [InlineData("x", "x")]
    [InlineData(null, "fallback")]
    [InlineData("  ", "fallback")]
    public void Text_falls_back_when_the_setting_is_missing_or_blank(string? value, string expected)
    {
        // Arrange
        var reader = new ReaderBuilder().With("Name", value).Build();

        // Act
        var text = reader.Text("Name", "fallback");

        // Assert
        text.ShouldBe(expected);
    }

    [Theory]
    [InlineData("9441", 9441)]
    [InlineData(" 7 ", 7)]
    [InlineData("-1", -1)]
    [InlineData(null, 9440)]
    public void Int_reads_a_whole_number_or_falls_back(string? value, int expected)
    {
        // Arrange
        var reader = new ReaderBuilder().With("Port", value).Build();

        // Act
        var number = reader.Int("Port", 9440);

        // Assert
        number.ShouldBe(expected);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void Int_names_the_setting_and_the_value_when_it_is_not_a_whole_number(string value)
    {
        // Arrange
        var reader = new ReaderBuilder().With("Listen:Port", value).Build().Section("Listen");

        // Act
        var read = () => { reader.Int("Port", 0); };

        // Assert
        var message = read.ShouldThrow<ConfigurationException>().Message;
        message.ShouldContain("'Listen:Port'");
        message.ShouldContain($"'{value}'");
    }

    [Theory]
    [InlineData("00:00:30", 30)]
    [InlineData("00:05:00", 300)]
    [InlineData("1.00:00:00", 86400)]
    [InlineData(null, 5)]
    public void Duration_reads_a_time_span_or_falls_back(string? value, int expectedSeconds)
    {
        // Arrange
        var reader = new ReaderBuilder().With("Wait", value).Build();

        // Act
        var duration = reader.Duration("Wait", TimeSpan.FromSeconds(5));

        // Assert
        duration.ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void Duration_names_the_setting_when_the_value_is_not_a_time_span()
    {
        // Arrange
        var reader = new ReaderBuilder().With("Wait", "soon").Build();

        // Act
        var read = () => { reader.Duration("Wait", TimeSpan.Zero); };

        // Assert
        read.ShouldThrow<ConfigurationException>().Message.ShouldContain("'Wait'");
    }

    [Fact]
    public void List_reads_an_array_in_order()
    {
        // Arrange
        var reader = new ReaderBuilder().With("Controllers:0", "a:1").With("Controllers:1", "b:2").With("Controllers:10", "c:3").Build();

        // Act
        var list = reader.List("Controllers");

        // Assert
        list.ShouldBe(["a:1", "b:2", "c:3"]);
    }

    [Theory]
    [InlineData("a:1,b:2", new[] { "a:1", "b:2" })]
    [InlineData(" a:1 , b:2 ,", new[] { "a:1", "b:2" })]
    [InlineData("a:1", new[] { "a:1" })]
    public void List_reads_one_comma_separated_value(string value, string[] expected)
    {
        // Arrange
        var reader = new ReaderBuilder().With("Controllers", value).Build();

        // Act
        var list = reader.List("Controllers");

        // Assert
        list.ShouldBe(expected);
    }

    [Fact]
    public void List_is_empty_when_the_setting_is_missing()
    {
        // Arrange
        var reader = new ReaderBuilder().Build();

        // Act
        var list = reader.List("Controllers");

        // Assert
        list.ShouldBeEmpty();
    }

    [Fact]
    public void Map_reads_the_entries_of_a_group()
    {
        // Arrange
        var reader = new ReaderBuilder().With("Labels:env", "prod").With("Labels:site", " a ").Build();

        // Act
        var map = reader.Map("Labels");

        // Assert
        map.ShouldBe(new Dictionary<string, string> { ["env"] = "prod", ["site"] = "a" });
    }

    [Fact]
    public void Items_reads_each_entry_of_a_list_of_groups_and_names_it_in_messages()
    {
        // Arrange
        var root = new ReaderBuilder().With("Controller:Tokens:0:Scope", "shop").With("Controller:Tokens:1:Hash", "abc").Build().Section("Controller");

        // Act
        var items = root.Items("Tokens");
        var missing = () => items[1].Required("Scope");

        // Assert
        items.Count.ShouldBe(2);
        items[0].Required("Scope").ShouldBe("shop");
        missing.ShouldThrow<ConfigurationException>().Message.ShouldContain("'Controller:Tokens:1:Scope'");
    }

    /// <summary>Builds a reader over in-memory settings.</summary>
    private sealed class ReaderBuilder
    {
        private readonly Dictionary<string, string?> _values = [];

        public ReaderBuilder With(string key, string? value)
        {
            _values[key] = value;
            return this;
        }

        public SettingsReader Build() =>
            new(new ConfigurationBuilder().AddInMemoryCollection(_values).Build());
    }
}
