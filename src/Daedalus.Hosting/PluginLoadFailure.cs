namespace Daedalus.Hosting;

/// <summary>单个插件 dll 的加载失败记录（FR-SHELL-004）：dll 文件名 + 异常。</summary>
/// <param name="DllName">加载失败的 dll 文件名（不含路径）。</param>
/// <param name="Exception">加载过程中抛出的异常。</param>
public sealed record PluginLoadFailure(string DllName, Exception Exception);
