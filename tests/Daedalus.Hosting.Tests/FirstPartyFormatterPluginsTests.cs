using Daedalus.Abstractions;

namespace Daedalus.Hosting.Tests;

/// <summary>
/// 第一方格式化器插件枚举验证（第 4 步完成标准）：把 Daedalus.Formatters.Json /
/// Daedalus.Formatters.Xml 的构建产物当作插件 dll 放入临时 plugins/ 目录，
/// 经真实 <see cref="PluginLoader"/> 扫描，验证能被 Hosting 枚举并可正常调用。
/// </summary>
public sealed class FirstPartyFormatterPluginsTests : IDisposable
{
    private readonly string _pluginsDirectory;
    private readonly string _tempRoot;

    public FirstPartyFormatterPluginsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Daedalus.Hosting.Tests", Guid.NewGuid().ToString("N"));
        _pluginsDirectory = Path.Combine(_tempRoot, "plugins");
        Directory.CreateDirectory(_pluginsDirectory);

        // 插件 dll 经 ProjectReference 带入本测试工程输出目录，从此处拷入临时 plugins/
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Daedalus.Formatters.Json.dll"),
            Path.Combine(_pluginsDirectory, "Daedalus.Formatters.Json.dll"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Daedalus.Formatters.Xml.dll"),
            Path.Combine(_pluginsDirectory, "Daedalus.Formatters.Xml.dll"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void LoadFromDirectory_第一方格式化器插件_入表且无失败()
    {
        PluginCatalog catalog = new PluginLoader().LoadFromDirectory(_pluginsDirectory);

        Assert.Empty(catalog.Failures);
        Assert.Empty(catalog.Tools);
        Assert.Equal(2, catalog.Formatters.Count);
        Assert.Contains(catalog.Formatters, formatter => formatter.FormatId == "json");
        Assert.Contains(catalog.Formatters, formatter => formatter.FormatId == "xml");
    }

    [Fact]
    public void LoadFromDirectory_第一方格式化器插件_枚举后可正常调用()
    {
        PluginCatalog catalog = new PluginLoader().LoadFromDirectory(_pluginsDirectory);

        IFormatter json = catalog.Formatters.Single(formatter => formatter.FormatId == "json");
        Assert.Equal("{\"a\":1}", json.Format("{ \"a\": 1 }", new FormatOptions(Minify: true, IndentSize: 4)));

        IFormatter xml = catalog.Formatters.Single(formatter => formatter.FormatId == "xml");
        Assert.True(xml.TryValidate("<root><a>1</a></root>", out string? error));
        Assert.Null(error);
    }
}
