using Daedalus.Abstractions;

namespace Daedalus.Hosting.Tests.Stubs;

/// <summary>测试桩格式化器插件：验证 PluginLoader 能发现并枚举 IFormatter 实现。</summary>
public sealed class StubFormatter : IFormatter
{
    public string FormatId => "stub";

    public string DisplayName => "Stub";

    public bool TryValidate(string input, out string? error)
    {
        error = null;
        return true;
    }

    public string Format(string input, FormatOptions options)
    {
        return input;
    }
}
