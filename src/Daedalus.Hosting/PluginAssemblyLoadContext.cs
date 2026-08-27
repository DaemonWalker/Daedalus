using System.Reflection;
using System.Runtime.Loader;

namespace Daedalus.Hosting;

/// <summary>
/// 单个插件 dll 的程序集加载上下文（架构 §5.1）：非收集。
/// 项目无插件热卸载需求；可收集上下文存在被 GC 在使用中卸载的风险
/// （Hermes 经 Jint 执行脚本时报"context 已 unload"的根因），故固定为非收集。
/// 契约程序集与共享日志库回落到宿主默认上下文解析，保证插件与宿主之间的类型同一性
/// （否则插件加载出第二份 Daedalus.Abstractions，ITool 将无法转换回宿主侧类型）。
/// </summary>
internal sealed class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Daedalus.Abstractions",
        "Serilog",
        // ITool 契约签名使用 IServiceCollection/IServiceProvider，宿主与插件必须共享同一份类型
        "Microsoft.Extensions.DependencyInjection.Abstractions",
    };

    private readonly AssemblyDependencyResolver _resolver;

    public PluginAssemblyLoadContext(string pluginMainAssemblyPath)
        : base(isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(pluginMainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not null && SharedAssemblies.Contains(assemblyName.Name))
        {
            return null;
        }

        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }
}
