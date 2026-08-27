using System.Windows.Forms;

using Daedalus.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Daedalus.Hosting.Tests.Stubs;

/// <summary>测试桩工具插件：验证 PluginLoader 能发现并枚举 ITool 实现。</summary>
public sealed class StubTool : ITool
{
    public ToolMetadata Metadata { get; } = new(
        "daedalus.tools.stub",
        "Stub 工具",
        "Hosting 测试桩工具插件。",
        new Version(1, 0, 0));

    public void RegisterServices(IServiceCollection services)
    {
    }

    public Control CreateView(IToolHost host, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(services);
        return new Panel();
    }
}
