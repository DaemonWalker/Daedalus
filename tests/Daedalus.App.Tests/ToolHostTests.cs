using Daedalus.Abstractions;
using Daedalus.App;

using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Daedalus.App.Tests;

public sealed class ToolHostTests : IDisposable
{
    private readonly string _baseDirectory;
    private readonly List<LogEvent> _events = [];
    private readonly Logger _logger;

    public ToolHostTests()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), "daedalus-app-tests", Guid.NewGuid().ToString("N"));
        _logger = new LoggerConfiguration()
            .WriteTo.Sink(new CollectingSink(_events.Add))
            .CreateLogger();
    }

    public void Dispose()
    {
        _logger.Dispose();
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetDataDirectory_目录不存在_创建目录并返回程序目录下的路径()
    {
        var host = new ToolHost(_baseDirectory, _logger, []);

        string directory = host.GetDataDirectory("daedalus.tools.stub");

        Assert.Equal(Path.Combine(_baseDirectory, "data", "daedalus.tools.stub"), directory);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public void GetDataDirectory_重复调用_幂等返回同一路径()
    {
        var host = new ToolHost(_baseDirectory, _logger, []);

        string first = host.GetDataDirectory("daedalus.tools.stub");
        string second = host.GetDataDirectory("daedalus.tools.stub");

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetDataDirectory_工具id为空_抛出ArgumentException()
    {
        var host = new ToolHost(_baseDirectory, _logger, []);

        Assert.ThrowsAny<ArgumentException>(() => host.GetDataDirectory(" "));
    }

    [Fact]
    public void GetLogger_写入日志_日志事件SourceContext为插件id()
    {
        var host = new ToolHost(_baseDirectory, _logger, []);

        host.GetLogger("daedalus.tools.stub").Information("测试消息");

        // SourceContext 承载插件 id：daedalus.json 的 logging.overrides 按此前缀匹配（架构 §6.2）
        LogEvent logEvent = Assert.Single(_events);
        Assert.True(logEvent.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? value));
        Assert.Equal("daedalus.tools.stub", Assert.IsType<ScalarValue>(value).Value);
    }

    [Fact]
    public void GetLogger_插件id为空_抛出ArgumentException()
    {
        var host = new ToolHost(_baseDirectory, _logger, []);

        // 故意传 null 验证参数校验，需 ! 抑制可空性警告
        Assert.ThrowsAny<ArgumentException>(() => host.GetLogger(null!));
    }

    [Fact]
    public void FindFormatter_格式已安装_大小写不敏感返回格式化器()
    {
        var formatter = new StubFormatter("json");
        var host = new ToolHost(_baseDirectory, _logger, [formatter]);

        IFormatter? found = host.FindFormatter("JSON");

        Assert.Same(formatter, found);
    }

    [Fact]
    public void FindFormatter_格式未安装_返回null()
    {
        var host = new ToolHost(_baseDirectory, _logger, [new StubFormatter("json")]);

        Assert.Null(host.FindFormatter("xml"));
    }

    [Fact]
    public void Formatters_返回构造时传入的格式化器表()
    {
        IReadOnlyList<IFormatter> formatters = [new StubFormatter("json"), new StubFormatter("xml")];
        var host = new ToolHost(_baseDirectory, _logger, formatters);

        Assert.Same(formatters, host.Formatters);
    }

    /// <summary>收集日志事件的测试用 Serilog 接收器。</summary>
    private sealed class CollectingSink(Action<LogEvent> collect) : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            collect(logEvent);
        }
    }

    /// <summary>仅携带格式 id 的桩格式化器。</summary>
    private sealed class StubFormatter(string formatId) : IFormatter
    {
        public string FormatId { get; } = formatId;

        public string DisplayName => FormatId.ToUpperInvariant();

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
}
