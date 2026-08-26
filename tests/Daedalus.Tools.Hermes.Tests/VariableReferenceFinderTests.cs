using Daedalus.Tools.Hermes.Variables;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>VariableReferenceFinder：悬浮编辑的命中检测（FR-HERMES-024），语法口径与 VariableResolver 一致。</summary>
public sealed class VariableReferenceFinderTests
{
    [Fact]
    public void FindAll_多个引用_按序返回名称与位置()
    {
        IReadOnlyList<VariableReference> references = VariableReferenceFinder.FindAll("{{host}}/api/{{token}}x");

        Assert.Equal(2, references.Count);
        Assert.Equal(new VariableReference("host", 0, 8), references[0]);
        Assert.Equal(new VariableReference("token", 13, 9), references[1]);
    }

    [Fact]
    public void FindAll_转义与未闭合与非法名_均不算引用()
    {
        IReadOnlyList<VariableReference> references = VariableReferenceFinder.FindAll(@"\{{host}} {{unclosed {{has space}} {{ok}}");

        Assert.Equal(["ok"], references.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void FindAt_下标落在引用内_返回该引用()
    {
        const string text = "ab{{host}}cd";

        // 落在 {{host}} 范围内（含边界字符）
        Assert.Equal("host", VariableReferenceFinder.FindAt(text, 2)?.Name);
        Assert.Equal("host", VariableReferenceFinder.FindAt(text, 9)?.Name);
    }

    [Fact]
    public void FindAt_下标不在引用内_返回null()
    {
        const string text = "ab{{host}}cd";

        Assert.Null(VariableReferenceFinder.FindAt(text, 0));
        Assert.Null(VariableReferenceFinder.FindAt(text, 11));
        Assert.Null(VariableReferenceFinder.FindAt(text, 10)); // 紧邻 }} 之后的 c
    }

    [Fact]
    public void FindAll_无引用_返回空表()
    {
        Assert.Empty(VariableReferenceFinder.FindAll("http://a.com/plain"));
    }
}
