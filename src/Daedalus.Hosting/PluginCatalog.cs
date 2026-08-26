using Daedalus.Abstractions;

namespace Daedalus.Hosting;

/// <summary>插件加载结果（架构 §5.1）：工具表、格式化器表与失败清单，交给 App 使用。</summary>
public sealed class PluginCatalog(
    IReadOnlyList<ITool> tools,
    IReadOnlyList<IFormatter> formatters,
    IReadOnlyList<PluginLoadFailure> failures)
{
    /// <summary>已加载的工具插件表。</summary>
    public IReadOnlyList<ITool> Tools { get; } = tools;

    /// <summary>已加载的格式化器插件表。</summary>
    public IReadOnlyList<IFormatter> Formatters { get; } = formatters;

    /// <summary>加载失败的 dll 清单（dll 名 + 异常），单个失败不中断其余插件加载。</summary>
    public IReadOnlyList<PluginLoadFailure> Failures { get; } = failures;
}
