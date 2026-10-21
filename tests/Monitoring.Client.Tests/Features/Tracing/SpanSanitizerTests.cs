using Monitoring.Client.Features.Tracing;
using Shouldly;
using Xunit;

namespace Monitoring.Client.Tests.Features.Tracing;

public class SpanSanitizerTests
{
    [Theory]
    [InlineData("https://shop.example/orders/42?token=abc&x=1", "https://shop.example/orders/42")]
    [InlineData("https://user:secret@shop.example/orders#top", "https://shop.example/orders")]
    [InlineData("http://shop:8080/a/b", "http://shop:8080/a/b")]
    [InlineData("/orders/42?token=abc", "/orders/42")]
    [InlineData("GET", "GET")]
    public void Url_keeps_the_address_up_to_its_path(string url, string expected)
    {
        // Arrange (nothing to prepare)

        // Act
        var cleaned = SpanSanitizer.Url(url);

        // Assert
        cleaned.ShouldBe(expected);
    }

    [Theory]
    [InlineData("SELECT * FROM users WHERE name = 'Bob O''Neil' AND age > 30", "SELECT * FROM users WHERE name = ? AND age > ?")]
    [InlineData("INSERT INTO t (a, b) VALUES (1, 'x')", "INSERT INTO t (a, b) VALUES (?, ?)")]
    [InlineData("SELECT col1 FROM table2", "SELECT col1 FROM table2")]
    [InlineData("UPDATE t SET price = 9.99", "UPDATE t SET price = ?")]
    public void Sql_replaces_the_literals_but_keeps_the_names(string sql, string expected)
    {
        // Arrange (nothing to prepare)

        // Act
        var cleaned = SpanSanitizer.Sql(sql);

        // Assert
        cleaned.ShouldBe(expected);
    }
}
