using Daedalus.Tools.Hermes.Response;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>ContentTypeFormatMapper：hermes.md §8 的 Content-Type → 格式 id 映射。</summary>
public sealed class ContentTypeFormatMapperTests
{
    [Theory]
    [InlineData("application/json", "json")]
    [InlineData("application/json; charset=utf-8", "json")]
    [InlineData("application/problem+json", "json")]
    [InlineData("Application/JSON", "json")]
    [InlineData("application/xml", "xml")]
    [InlineData("text/xml", "xml")]
    [InlineData("application/atom+xml; charset=utf-8", "xml")]
    [InlineData("text/html", null)]
    [InlineData("text/plain", null)]
    [InlineData("text/json", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Map_各种ContentType_映射到对应格式id(string? contentType, string? expected)
    {
        Assert.Equal(expected, ContentTypeFormatMapper.Map(contentType));
    }
}
