using Daedalus.Tools.Hermes.Editing;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>FindMatchIndex：查找条匹配核心（忽略大小写子串匹配、环绕导航）。</summary>
public sealed class FindMatchIndexTests
{
    [Fact]
    public void FindAll_大小写混合_全部命中且起点升序()
    {
        int[] matches = FindMatchIndex.FindAll("Token abc TOKEN abc token", "token");

        Assert.Equal([0, 10, 20], matches);
    }

    [Fact]
    public void FindAll_可重叠的候选_按词长非重叠推进()
    {
        int[] matches = FindMatchIndex.FindAll("aaaaa", "aa");

        Assert.Equal([0, 2], matches);
    }

    [Fact]
    public void FindAll_空词或无命中_返回空()
    {
        Assert.Empty(FindMatchIndex.FindAll("abc", ""));
        Assert.Empty(FindMatchIndex.FindAll("abc", "abcd"));
        Assert.Empty(FindMatchIndex.FindAll("abc", "xyz"));
    }

    [Fact]
    public void WrapNext_到末尾_回绕到开头()
    {
        Assert.Equal(1, FindMatchIndex.WrapNext(0, 3));
        Assert.Equal(0, FindMatchIndex.WrapNext(2, 3));
        Assert.Equal(0, FindMatchIndex.WrapNext(-1, 3));
        Assert.Equal(-1, FindMatchIndex.WrapNext(0, 0));
    }

    [Fact]
    public void WrapPrevious_到开头_回绕到末尾()
    {
        Assert.Equal(1, FindMatchIndex.WrapPrevious(2, 3));
        Assert.Equal(2, FindMatchIndex.WrapPrevious(0, 3));
        Assert.Equal(2, FindMatchIndex.WrapPrevious(-1, 3));
        Assert.Equal(-1, FindMatchIndex.WrapPrevious(0, 0));
    }

    [Fact]
    public void IndexAtOrAfter_中间位置_取首个不早于位置的命中()
    {
        int[] matches = [2, 5, 9];

        Assert.Equal(0, FindMatchIndex.IndexAtOrAfter(matches, 0));
        Assert.Equal(1, FindMatchIndex.IndexAtOrAfter(matches, 3));
        Assert.Equal(2, FindMatchIndex.IndexAtOrAfter(matches, 9));
    }

    [Fact]
    public void IndexAtOrAfter_全部命中在位置之前_回绕到首个()
    {
        Assert.Equal(0, FindMatchIndex.IndexAtOrAfter([2, 5, 9], 10));
        Assert.Equal(-1, FindMatchIndex.IndexAtOrAfter([], 0));
    }
}
