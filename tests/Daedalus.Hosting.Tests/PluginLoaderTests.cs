using Daedalus.Abstractions;

namespace Daedalus.Hosting.Tests;

/// <summary>
/// PluginLoader 测试：以独立桩插件工程（Daedalus.Hosting.Tests.Stubs /
/// Daedalus.Hosting.Tests.ThrowingStubs）的构建产物充当插件 dll，
/// 拷贝到临时 plugins/ 目录后验证加载、枚举与失败隔离（架构 §5.1、FR-SHELL-004）。
/// </summary>
public sealed class PluginLoaderTests : IDisposable
{
    private const string StubPluginFile = "Daedalus.Hosting.Tests.Stubs.dll";
    private const string ThrowingPluginFile = "Daedalus.Hosting.Tests.ThrowingStubs.dll";

    private readonly string _pluginsDirectory;
    private readonly string _tempRoot;

    public PluginLoaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Daedalus.Hosting.Tests", Guid.NewGuid().ToString("N"));
        _pluginsDirectory = Path.Combine(_tempRoot, "plugins");
        Directory.CreateDirectory(_pluginsDirectory);
    }

    public void Dispose()
    {
        // 加载器经内存流加载程序集，dll 不被锁定，临时目录可直接删除；
        // 清理失败不影响测试结果，尽力而为即可
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
    public void LoadFromDirectory_正常桩插件_发现并枚举工具与格式化器()
    {
        CopyToPluginsDirectory(StubPluginFile);

        PluginCatalog catalog = new PluginLoader().LoadFromDirectory(_pluginsDirectory);

        Assert.Empty(catalog.Failures);
        ITool tool = Assert.Single(catalog.Tools);
        Assert.Equal("daedalus.tools.stub", tool.Metadata.Id);
        Assert.Equal("Stub 工具", tool.Metadata.DisplayName);
        IFormatter formatter = Assert.Single(catalog.Formatters);
        Assert.Equal("stub", formatter.FormatId);
    }

    [Fact]
    public void LoadFromDirectory_损坏dll_记入失败清单且不影响其他插件()
    {
        CopyToPluginsDirectory(StubPluginFile);
        File.WriteAllText(Path.Combine(_pluginsDirectory, "Broken.Plugin.dll"), "这不是一个有效的 .NET 程序集");

        PluginCatalog catalog = new PluginLoader().LoadFromDirectory(_pluginsDirectory);

        Assert.Single(catalog.Tools);
        Assert.Single(catalog.Formatters);
        PluginLoadFailure failure = Assert.Single(catalog.Failures);
        Assert.Equal("Broken.Plugin.dll", failure.DllName);
        Assert.NotNull(failure.Exception);
    }

    [Fact]
    public void LoadFromDirectory_插件类型实例化抛异常_记入失败清单且其余插件正常()
    {
        CopyToPluginsDirectory(StubPluginFile);
        CopyToPluginsDirectory(ThrowingPluginFile);

        PluginCatalog catalog = new PluginLoader().LoadFromDirectory(_pluginsDirectory);

        Assert.Single(catalog.Tools);
        Assert.Single(catalog.Formatters);
        PluginLoadFailure failure = Assert.Single(catalog.Failures);
        Assert.Equal(ThrowingPluginFile, failure.DllName);
    }

    [Fact]
    public void LoadFromDirectory_dll无任何插件实现_不计失败()
    {
        // Daedalus.Hosting.dll 本身不含任何插件实现，可充当"非插件 dll"
        CopyToPluginsDirectory("Daedalus.Hosting.dll");

        PluginCatalog catalog = new PluginLoader().LoadFromDirectory(_pluginsDirectory);

        Assert.Empty(catalog.Tools);
        Assert.Empty(catalog.Formatters);
        Assert.Empty(catalog.Failures);
    }

    [Fact]
    public void LoadFromDirectory_目录不存在_返回空结果()
    {
        PluginCatalog catalog = new PluginLoader().LoadFromDirectory(Path.Combine(_tempRoot, "not-exists"));

        Assert.Empty(catalog.Tools);
        Assert.Empty(catalog.Formatters);
        Assert.Empty(catalog.Failures);
    }

    private void CopyToPluginsDirectory(string assemblyFileName)
    {
        string source = Path.Combine(AppContext.BaseDirectory, assemblyFileName);
        File.Copy(source, Path.Combine(_pluginsDirectory, assemblyFileName));
    }
}
