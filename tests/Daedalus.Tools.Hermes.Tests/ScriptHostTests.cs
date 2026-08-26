using Daedalus.Tools.Hermes.History;
using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.Scripting;
using Daedalus.Tools.Hermes.Settings;
using Daedalus.Tools.Hermes.Variables;

using Serilog.Core;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>ScriptHost 测试（hermes.md §7 / §12）：pm 全 API、set 立即持久化、沙箱限制、异常隔离。</summary>
public sealed class ScriptHostTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "hermes-scripthost-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private static HopResponse JsonResponse() => new(
        200,
        "OK",
        [new NameValuePair("Content-Type", "application/json")],
        "{\"token\":\"abc\",\"count\":3}",
        12);

    private async Task<EnvironmentStore> SeedEnvironmentAsync()
    {
        var store = new EnvironmentStore(_dataDirectory);
        await store.SaveAsync(new EnvironmentData
        {
            ActiveId = "dev",
            Environments = [new HermesEnvironment
            {
                Id = "dev",
                Name = "开发环境",
                Variables = [new EnvironmentVariable("host", "http://localhost:8080")],
            }],
        });
        return store;
    }

    private ScriptHost CreateHost(EnvironmentStore store) => new(store, Logger.None);

    private static Task<ScriptExecutionResult> RunAsync(
        ScriptHost host,
        string script,
        EnvironmentData environmentData,
        HermesSettings? settings = null,
        HopResponse? response = null) =>
        host.RunAsync(script, response ?? JsonResponse(), environmentData, settings ?? HermesSettings.Default);

    [Fact]
    public async Task RunAsync_set_写入后立即持久化到磁盘()
    {
        EnvironmentStore store = await SeedEnvironmentAsync();
        EnvironmentData data = (await store.LoadAsync()).Data;

        ScriptExecutionResult result = await RunAsync(CreateHost(store), "pm.environment.set('token', 'abc');", data);

        Assert.Null(result.Error);
        Assert.NotNull(result.UpdatedEnvironmentData);
        Assert.Equal("abc", result.UpdatedEnvironmentData.Environments[0].Variables.First(v => v.Key == "token").Value);
        // 端到端落盘验证：换一个 Store 实例从磁盘重读，值必须在
        EnvironmentData reloaded = (await new EnvironmentStore(_dataDirectory).LoadAsync()).Data;
        Assert.Equal("abc", reloaded.Environments[0].Variables.First(v => v.Key == "token").Value);
        Assert.Contains(result.MutationLog, m => m.Contains("pm.environment.set") && m.Contains("token"));
    }

    [Fact]
    public async Task RunAsync_get_读到脚本内刚set的值()
    {
        EnvironmentStore store = await SeedEnvironmentAsync();
        EnvironmentData data = (await store.LoadAsync()).Data;

        ScriptExecutionResult result = await RunAsync(CreateHost(store),
            "pm.environment.set('a', '1'); pm.environment.set('b', pm.environment.get('a') + '2');", data);

        Assert.Null(result.Error);
        Assert.Equal("12", result.UpdatedEnvironmentData!.Environments[0].Variables.First(v => v.Key == "b").Value);
    }

    [Fact]
    public async Task RunAsync_get_不存在返回undefined()
    {
        EnvironmentStore store = await SeedEnvironmentAsync();
        EnvironmentData data = (await store.LoadAsync()).Data;

        ScriptExecutionResult result = await RunAsync(CreateHost(store),
            "pm.environment.set('x', pm.environment.get('nope') === undefined ? 'undef' : 'def');", data);

        Assert.Null(result.Error);
        Assert.Equal("undef", result.UpdatedEnvironmentData!.Environments[0].Variables.First(v => v.Key == "x").Value);
    }

    [Fact]
    public async Task RunAsync_unset_删除并持久化()
    {
        EnvironmentStore store = await SeedEnvironmentAsync();
        EnvironmentData data = (await store.LoadAsync()).Data;

        ScriptExecutionResult result = await RunAsync(CreateHost(store), "pm.environment.unset('host');", data);

        Assert.Null(result.Error);
        Assert.DoesNotContain(result.UpdatedEnvironmentData!.Environments[0].Variables, v => v.Key == "host");
        EnvironmentData reloaded = (await new EnvironmentStore(_dataDirectory).LoadAsync()).Data;
        Assert.DoesNotContain(reloaded.Environments[0].Variables, v => v.Key == "host");
    }

    [Fact]
    public async Task RunAsync_未启用环境_set报错且不写盘()
    {
        var store = new EnvironmentStore(_dataDirectory); // 空数据，无启用环境

        ScriptExecutionResult result = await RunAsync(CreateHost(store), "pm.environment.set('a', '1');", EnvironmentData.Empty);

        Assert.NotNull(result.Error);
        Assert.Contains("未启用", result.Error);
        Assert.Null(result.UpdatedEnvironmentData);
    }

    [Fact]
    public async Task RunAsync_response全子集_均可访问()
    {
        EnvironmentStore store = await SeedEnvironmentAsync();
        EnvironmentData data = (await store.LoadAsync()).Data;
        const string script = """
            pm.environment.set('code', String(pm.response.code));
            pm.environment.set('token', pm.response.json().token);
            pm.environment.set('count', String(pm.response.json().count));
            pm.environment.set('ct', pm.response.headers.get('content-type'));
            pm.environment.set('body', pm.response.text());
            """;

        ScriptExecutionResult result = await RunAsync(CreateHost(store), script, data);

        Assert.Null(result.Error);
        List<EnvironmentVariable> variables = result.UpdatedEnvironmentData!.Environments[0].Variables;
        Assert.Equal("200", variables.First(v => v.Key == "code").Value);
        Assert.Equal("abc", variables.First(v => v.Key == "token").Value);
        Assert.Equal("3", variables.First(v => v.Key == "count").Value);
        Assert.Equal("application/json", variables.First(v => v.Key == "ct").Value);
        Assert.Equal("{\"token\":\"abc\",\"count\":3}", variables.First(v => v.Key == "body").Value);
    }

    [Fact]
    public async Task RunAsync_json解析非JSON_报错()
    {
        EnvironmentStore store = await SeedEnvironmentAsync();
        EnvironmentData data = (await store.LoadAsync()).Data;
        var response = new HopResponse(200, "OK", [], "not json", 1);

        ScriptExecutionResult result = await RunAsync(CreateHost(store), "pm.response.json();", data, response: response);

        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task RunAsync_脚本异常_隔离进结果不抛出()
    {
        EnvironmentStore store = await SeedEnvironmentAsync();
        EnvironmentData data = (await store.LoadAsync()).Data;

        ScriptExecutionResult result = await RunAsync(CreateHost(store),
            "pm.environment.set('a', '1'); throw new Error('炸了');", data);

        // 异常被捕获进结果（FR-HERMES-043）；出错前已执行的 set 仍然生效落盘
        Assert.NotNull(result.Error);
        Assert.Contains("炸了", result.Error);
        Assert.Equal("1", result.UpdatedEnvironmentData!.Environments[0].Variables.First(v => v.Key == "a").Value);
    }

    [Fact]
    public async Task RunAsync_内存超限_报错()
    {
        EnvironmentStore store = await SeedEnvironmentAsync();
        EnvironmentData data = (await store.LoadAsync()).Data;

        ScriptExecutionResult result = await RunAsync(CreateHost(store),
            "var s = 'x'; for (var i = 0; i < 24; i++) { s = s + s; }", data);

        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task RunAsync_超时_报错()
    {
        EnvironmentStore store = await SeedEnvironmentAsync();
        EnvironmentData data = (await store.LoadAsync()).Data;
        HermesSettings settings = HermesSettings.Default with { ScriptTimeoutMs = 300 };

        ScriptExecutionResult result = await RunAsync(CreateHost(store), "while (true) { }", data, settings);

        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("pm.sendRequest('http://a');")]
    [InlineData("pm.test('t', function () {});")]
    [InlineData("pm.globals.get('x');")]
    public async Task RunAsync_未实现API_抛未实现错误(string script)
    {
        EnvironmentStore store = await SeedEnvironmentAsync();
        EnvironmentData data = (await store.LoadAsync()).Data;

        ScriptExecutionResult result = await RunAsync(CreateHost(store), script, data);

        Assert.NotNull(result.Error);
        Assert.Contains("未实现", result.Error);
    }

    [Fact]
    public async Task RunAsync_无环境写操作_不触发持久化()
    {
        EnvironmentStore store = await SeedEnvironmentAsync();
        EnvironmentData data = (await store.LoadAsync()).Data;

        ScriptExecutionResult result = await RunAsync(CreateHost(store), "var x = pm.response.code;", data);

        Assert.Null(result.Error);
        Assert.Null(result.UpdatedEnvironmentData);
        Assert.Empty(result.MutationLog);
    }
}
