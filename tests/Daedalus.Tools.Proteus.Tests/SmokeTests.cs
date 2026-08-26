using System.Reflection;

namespace Daedalus.Tools.Proteus.Tests;

/// <summary>脚手架阶段的占位冒烟测试：验证被测程序集可被引用并加载。后续步骤由真实测试取代。</summary>
public class SmokeTests
{
    [Fact]
    public void Load_被测程序集_加载成功()
    {
        Assembly assembly = Assembly.Load("Daedalus.Tools.Proteus");

        Assert.NotNull(assembly);
    }
}
