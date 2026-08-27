using System.Reflection;
using System.Runtime.Loader;

using Daedalus.Abstractions;

using Serilog;

namespace Daedalus.Hosting;

/// <summary>
/// 插件加载器（架构 §5.1）：扫描插件目录下平铺的 *.dll（不递归子目录），逐个装入独立非收集的
/// <see cref="PluginAssemblyLoadContext"/>，反射查找 <see cref="ITool"/> / <see cref="IFormatter"/>
/// 的公开实现。单个 dll 失败记入失败清单并继续加载其余插件（FR-SHELL-004）。
/// 成功创建的加载上下文全部收集进 <see cref="PluginCatalog"/>，供诊断/排查用。
/// </summary>
public sealed class PluginLoader
{
    private readonly ILogger? _logger;

    /// <param name="logger">宿主日志器；为 null 时只做失败清单记录、不写日志（主要用于测试）。</param>
    public PluginLoader(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 扫描 <paramref name="pluginsDirectory"/> 下平铺的 *.dll 并加载全部插件。
    /// 目录不存在时返回空结果，不视为失败（便于绿色单目录发布首次运行）。
    /// </summary>
    public PluginCatalog LoadFromDirectory(string pluginsDirectory)
    {
        var tools = new List<ITool>();
        var formatters = new List<IFormatter>();
        var failures = new List<PluginLoadFailure>();
        var loadContexts = new List<AssemblyLoadContext>();

        if (!Directory.Exists(pluginsDirectory))
        {
            return new PluginCatalog(tools, formatters, failures, loadContexts);
        }

        foreach (string dllPath in Directory.EnumerateFiles(pluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            string dllName = Path.GetFileName(dllPath);
            try
            {
                loadContexts.Add(LoadPlugin(dllPath, tools, formatters));
            }
            catch (Exception ex)
            {
                // 插件边界按架构 §8 兜底：单个 dll 的任何失败（坏程序集、类型解析失败、
                // 实例化抛异常等）都不允许中断其余插件的加载
                _logger?.Error(ex, "插件 {DllName} 加载失败", dllName);
                failures.Add(new PluginLoadFailure(dllName, ex));
            }
        }

        return new PluginCatalog(tools, formatters, failures, loadContexts);
    }

    private static PluginAssemblyLoadContext LoadPlugin(string dllPath, List<ITool> tools, List<IFormatter> formatters)
    {
        var context = new PluginAssemblyLoadContext(dllPath);

        // 经内存流加载程序集，避免应用运行期间锁定 plugins/ 下的 dll 文件
        Assembly assembly;
        using (FileStream stream = File.OpenRead(dllPath))
        {
            assembly = context.LoadFromStream(stream);
        }

        foreach (Type type in assembly.GetExportedTypes())
        {
            if (!type.IsClass || type.IsAbstract)
            {
                continue;
            }

            if (type.IsAssignableTo(typeof(ITool)) && Activator.CreateInstance(type) is ITool tool)
            {
                tools.Add(tool);
            }
            else if (type.IsAssignableTo(typeof(IFormatter)) && Activator.CreateInstance(type) is IFormatter formatter)
            {
                formatters.Add(formatter);
            }
        }

        return context;
    }
}
