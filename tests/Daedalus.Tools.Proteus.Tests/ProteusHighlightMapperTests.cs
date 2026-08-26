namespace Daedalus.Tools.Proteus.Tests;

/// <summary>ProteusHighlightMapper 测试：格式 id → 高亮种类映射（proteus.md §5）。</summary>
public class ProteusHighlightMapperTests
{
    [Theory]
    [InlineData("json", ProteusHighlightKind.Json)]
    [InlineData("JSON", ProteusHighlightKind.Json)]
    [InlineData("xml", ProteusHighlightKind.Xml)]
    [InlineData("Xml", ProteusHighlightKind.Xml)]
    [InlineData("yaml", ProteusHighlightKind.None)]
    [InlineData(null, ProteusHighlightKind.None)]
    public void Map_格式id_返回对应高亮种类(string? formatId, ProteusHighlightKind expected)
    {
        ProteusHighlightKind result = ProteusHighlightMapper.Map(formatId);

        Assert.Equal(expected, result);
    }
}
