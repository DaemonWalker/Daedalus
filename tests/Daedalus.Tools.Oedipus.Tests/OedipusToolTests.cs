using System.Windows.Forms;

using Daedalus.Abstractions;

using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace Daedalus.Tools.Oedipus.Tests;

/// <summary>OedipusTool 测试：插件契约（元数据、RegisterServices / CreateView 冒烟）。</summary>
public class OedipusToolTests
{
    [Fact]
    public void Metadata_插件元数据_id与显示名符合约定()
    {
        var tool = new OedipusTool();

        Assert.Equal("daedalus.tools.oedipus", tool.Metadata.Id);
        Assert.False(string.IsNullOrWhiteSpace(tool.Metadata.DisplayName));
    }

    [Fact]
    public void RegisterServices与CreateView_预置宿主服务后_成功创建视图()
    {
        var tool = new OedipusTool();
        var host = new FakeToolHost(Path.Combine(Path.GetTempPath(), "daedalus-oedipus-tool-" + Guid.NewGuid().ToString("N")));
        var services = new ServiceCollection();
        // 照 App 组合根约定（架构 §6.0）：以实例形式预置 IToolHost 与按插件 id 打好上下文的 ILogger
        services.AddSingleton<IToolHost>(host);
        services.AddSingleton<ILogger>(Serilog.Core.Logger.None);

        tool.RegisterServices(services);
        ServiceProvider provider = services.BuildServiceProvider();
        using Control view = tool.CreateView(host, provider);

        Assert.NotNull(view);
    }

    /// <summary>测试用宿主桩：数据目录指向临时目录，无格式化器。</summary>
    private sealed class FakeToolHost : IToolHost
    {
        private readonly string _dataDirectory;

        public FakeToolHost(string dataDirectory)
        {
            _dataDirectory = dataDirectory;
        }

        public IReadOnlyList<IFormatter> Formatters => [];

        public string GetDataDirectory(string toolId)
        {
            Directory.CreateDirectory(_dataDirectory);
            return _dataDirectory;
        }

        public ILogger GetLogger(string pluginId)
        {
            return Serilog.Core.Logger.None;
        }

        public IFormatter? FindFormatter(string formatId)
        {
            return null;
        }
    }
}
