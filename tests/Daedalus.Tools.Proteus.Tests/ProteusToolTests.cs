namespace Daedalus.Tools.Proteus.Tests;

/// <summary>ProteusTool 测试：插件契约（元数据 id 与显示名）。</summary>
public class ProteusToolTests
{
    [Fact]
    public void Metadata_插件元数据_id与显示名符合约定()
    {
        var tool = new ProteusTool();

        Assert.Equal("daedalus.tools.proteus", tool.Metadata.Id);
        Assert.False(string.IsNullOrWhiteSpace(tool.Metadata.DisplayName));
    }
}
