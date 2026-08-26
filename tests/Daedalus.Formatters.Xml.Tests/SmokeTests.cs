using System.Reflection;

namespace Daedalus.Formatters.Xml.Tests;

/// <summary>脚手架阶段的占位冒烟测试：验证被测程序集可被引用并加载。第 4 步由真实测试取代。</summary>
public class SmokeTests
{
    [Fact]
    public void Load_被测程序集_加载成功()
    {
        Assembly assembly = Assembly.Load("Daedalus.Formatters.Xml");

        Assert.NotNull(assembly);
    }
}
