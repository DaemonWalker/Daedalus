using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Editing;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>QueryParamMapper：URL query ↔ Params 编辑表。</summary>
public sealed class QueryParamMapperTests
{
    [Fact]
    public void Parse_带query_解析为键值表()
    {
        List<KeyValueEntry> entries = QueryParamMapper.Parse("http://a.com/p?x=1&y=%E4%B8%AD&flag");

        Assert.Equal(
            [new KeyValueEntry("x", "1"), new KeyValueEntry("y", "中"), new KeyValueEntry("flag", "")],
            entries);
    }

    [Fact]
    public void Parse_无query或空query_返回空表()
    {
        Assert.Empty(QueryParamMapper.Parse("http://a.com/p"));
        Assert.Empty(QueryParamMapper.Parse("http://a.com/p?"));
    }

    [Fact]
    public void Parse_带片段_query到井号为止()
    {
        List<KeyValueEntry> entries = QueryParamMapper.Parse("http://a.com/p?x=1#section");

        Assert.Equal([new KeyValueEntry("x", "1")], entries);
    }

    [Fact]
    public void Apply_替换query_保留路径与片段()
    {
        string url = QueryParamMapper.Apply("http://a.com/p?old=1#s",
            [new KeyValueEntry("x", "1"), new KeyValueEntry("y", "中 文")]);

        Assert.Equal("http://a.com/p?x=1&y=%E4%B8%AD%20%E6%96%87#s", url);
    }

    [Fact]
    public void Apply_禁用或空键的项不参与拼接()
    {
        string url = QueryParamMapper.Apply("http://a.com/p",
            [new KeyValueEntry("a", "1"), new KeyValueEntry("b", "2", Enabled: false), new KeyValueEntry("", "3")]);

        Assert.Equal("http://a.com/p?a=1", url);
    }

    [Fact]
    public void Apply_空表_移除原query()
    {
        Assert.Equal("http://a.com/p", QueryParamMapper.Apply("http://a.com/p?x=1", []));
    }

    [Fact]
    public void ParseApply_往返_内容不变()
    {
        const string original = "http://a.com/p?x=1&y=%E4%B8%AD";

        string roundTripped = QueryParamMapper.Apply(original, QueryParamMapper.Parse(original));

        Assert.Equal(original, roundTripped);
    }
}
