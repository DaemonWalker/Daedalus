using Daedalus.Abstractions;

using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace Daedalus.App;

/// <summary>
/// 每工具独立 ServiceProvider 的构建结果（架构 §6，step 14）：容器表 + 容器构建失败清单。
/// </summary>
internal sealed class ToolContainerRegistry
{
    private readonly Dictionary<ITool, IServiceProvider> _providers = new();

    /// <summary>容器构建失败的工具清单（工具 id + 异常），与插件加载失败同款隔离：不中断其他工具。</summary>
    public List<ToolContainerFailure> Failures { get; } = [];

    /// <summary>查找工具的容器；构建失败（或未注册）的工具返回 null。</summary>
    public IServiceProvider? Find(ITool tool) => _providers.TryGetValue(tool, out IServiceProvider? provider) ? provider : null;

    /// <summary>
    /// 为每个工具构建独立 ServiceProvider：新 ServiceCollection → 以实例形式预置宿主服务
    /// （<see cref="IToolHost"/> 与按该插件 id 打好 SourceContext 的 ILogger）→ 调插件的
    /// <see cref="ITool.RegisterServices"/> → BuildServiceProvider。单个工具失败只记清单，不中断其余。
    /// </summary>
    public static ToolContainerRegistry Build(IReadOnlyList<ITool> tools, IToolHost host, ILogger logger)
    {
        var registry = new ToolContainerRegistry();
        foreach (ITool tool in tools)
        {
            try
            {
                var services = new ServiceCollection();
                // 以实例形式预置：MS.DI 只 Dispose 自建对象，外部实例（宿主服务）不受容器释放影响
                services.AddSingleton(host);
                services.AddSingleton(host.GetLogger(tool.Metadata.Id));
                tool.RegisterServices(services);
                registry._providers[tool] = services.BuildServiceProvider();
            }
            catch (Exception ex)
            {
                // FR-SHELL-004 同款隔离：一个工具注册/构建失败不影响其他工具，打开时再提示
                logger.Error(ex, "工具 {ToolId} 服务注册/容器构建失败", tool.Metadata.Id);
                registry.Failures.Add(new ToolContainerFailure(tool.Metadata.Id, tool.Metadata.DisplayName, ex));
            }
        }

        return registry;
    }
}

/// <summary>工具容器构建失败记录（工具 id + 显示名 + 异常）。</summary>
internal sealed record ToolContainerFailure(string ToolId, string DisplayName, Exception Exception);
