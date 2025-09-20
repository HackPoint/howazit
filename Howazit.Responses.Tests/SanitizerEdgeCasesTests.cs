using FluentAssertions;
using Howazit.Responses.Infrastructure.Sanitization;
using Xunit;

namespace Howazit.Responses.Tests;

public class SanitizerEdgeCasesTests {
    [Fact]
    public void SanitizeNullIsEmpty() {
        var s = new HtmlSanitizer();
        s.Sanitize(null).Should().BeEmpty();
    }

    [Fact]
    public void SanitizeRemovesScriptTags() {
        var s = new HtmlSanitizer();
        var input = "<b>hi</b><script>alert('x')</script>";
        s.Sanitize(input).Should().Be("hi");
    }

    [Fact]
    public void SanitizeInPlaceHandlesMixedDictionary() {
        var s = new HtmlSanitizer();
        var dict = new Dictionary<string, object?> {
            ["a"] = "<i>ok</i>",
            ["b"] = 42,
            ["c"] = null
        };
        s.SanitizeInPlace(dict);
        dict["a"].Should().Be("ok");
        dict["b"].Should().Be(42);
        dict["c"].Should().BeNull();
    }
    
    [Fact]
    public void SanitizeRemovesScriptTagsAndKeepsText()
    {
        var s = new HtmlSanitizer();
        var input = "<b>Hello</b><script>alert('x')</script>world";
        var output = s.Sanitize(input);

        output.Should().Contain("Hello");
        output.Should().Contain("world");
        output.Should().NotContain("<script");
        output.Should().NotContain("alert(");
    }
    
    [Fact]
    public void SanitizeInPlaceOnlySanitizesStringsLeavesOthers()
    {
        var s = new HtmlSanitizer();

        var dict = new Dictionary<string, object?>
        {
            ["safe"] = "hello",
            ["xss"] = "<img src=x onerror=alert(1)>",
            ["num"] = 123,
            ["nested"] = new Dictionary<string, object?> { ["inner"] = "<script>boom()</script>" }
        };

        s.SanitizeInPlace(dict);

        dict["safe"].Should().Be("hello");
        dict["xss"]!.ToString()!.ToLowerInvariant().Should().NotContain("onerror");
        dict["xss"]!.ToString()!.ToLowerInvariant().Should().NotContain("<script");
        dict["num"].Should().Be(123); // untouched

        var nested = (Dictionary<string, object?>)dict["nested"]!;
        nested["inner"]!.ToString()!.ToLowerInvariant().Should().NotContain("<script");
    }
}