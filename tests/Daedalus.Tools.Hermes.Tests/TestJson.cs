using System.Text.Json;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>测试辅助：模型含 List 成员时 record 相等性是引用比较，往返一致性改用序列化结果比较。</summary>
internal static class TestJson
{
    internal static void Equal<T>(T expected, T actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
}
