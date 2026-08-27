using System.Windows.Forms;

using Daedalus.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Daedalus.Hosting.Tests.ThrowingStubs;

/// <summary>测试桩工具插件：静态构造函数故意抛异常，验证 PluginLoader 的失败隔离。</summary>
public sealed class ThrowingTool : ITool
{
    static ThrowingTool()
    {
        throw new InvalidOperationException("桩插件：静态构造故意抛异常，验证加载失败隔离。");
    }

    public ToolMetadata Metadata => throw new NotSupportedException();

    public void RegisterServices(IServiceCollection services)
    {
        throw new NotSupportedException();
    }

    public Control CreateView(IToolHost host, IServiceProvider services)
    {
        throw new NotSupportedException();
    }
}
