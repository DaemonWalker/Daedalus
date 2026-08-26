using Daedalus.Tools.Hermes.Variables;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>
/// VariableResolver 测试（hermes.md §6/§12）：替换、未定义保留 + 清单、转义、无启用环境、
/// 停用变量、未闭合/非法名按字面量保留。
/// </summary>
public sealed class VariableResolverTests
{
    private static readonly HermesEnvironment Environment = new()
    {
        Id = "dev",
        Name = "开发环境",
        Variables =
        [
            new EnvironmentVariable("host", "http://localhost:8080"),
            new EnvironmentVariable("token", "abc", Secret: true),
            new EnvironmentVariable("disabled", "nope", Enabled: false),
        ],
    };

    private readonly VariableResolver _resolver = new();

    [Fact]
    public void Resolve_已定义变量_替换为变量值()
    {
        VariableResolutionResult result = _resolver.Resolve("{{host}}/api/login", Environment);

        Assert.Equal("http://localhost:8080/api/login", result.Text);
        Assert.False(result.HasUndefinedVariables);
    }

    [Fact]
    public void Resolve_多个变量且同一变量重复出现_全部替换()
    {
        VariableResolutionResult result = _resolver.Resolve("{{host}}/a?x={{host}}&t={{token}}", Environment);

        Assert.Equal("http://localhost:8080/a?x=http://localhost:8080&t=abc", result.Text);
    }

    [Fact]
    public void Resolve_未定义变量_原样保留并列入清单且去重()
    {
        VariableResolutionResult result = _resolver.Resolve("{{host}}/{{missing}}?m={{missing}}", Environment);

        Assert.Equal("http://localhost:8080/{{missing}}?m={{missing}}", result.Text);
        Assert.Equal(["missing"], result.UndefinedVariables);
    }

    [Fact]
    public void Resolve_变量已停用_视为未定义()
    {
        VariableResolutionResult result = _resolver.Resolve("{{disabled}}", Environment);

        Assert.Equal("{{disabled}}", result.Text);
        Assert.Equal(["disabled"], result.UndefinedVariables);
    }

    [Fact]
    public void Resolve_无启用环境_所有变量视为未定义()
    {
        VariableResolutionResult result = _resolver.Resolve("{{host}}/api", null);

        Assert.Equal("{{host}}/api", result.Text);
        Assert.Equal(["host"], result.UndefinedVariables);
    }

    [Fact]
    public void Resolve_转义反斜杠双花括号_输出字面量且不做解析()
    {
        VariableResolutionResult result = _resolver.Resolve(@"\{{host}}", Environment);

        Assert.Equal("{{host}}", result.Text);
        Assert.False(result.HasUndefinedVariables);
    }

    [Fact]
    public void Resolve_转义与真实变量混合_分别处理()
    {
        VariableResolutionResult result = _resolver.Resolve(@"\{{host}} 与 {{host}}", Environment);

        Assert.Equal("{{host}} 与 http://localhost:8080", result.Text);
    }

    [Fact]
    public void Resolve_未闭合双花括号_按字面量保留()
    {
        VariableResolutionResult result = _resolver.Resolve("a {{host", Environment);

        Assert.Equal("a {{host", result.Text);
        Assert.False(result.HasUndefinedVariables);
    }

    [Fact]
    public void Resolve_外层变量名非法内层合法_外层字面量保留内层仍被替换()
    {
        // "{{host b {{host}}" 以第一个 "}}" 收尾，变量名含空格非法 → 按字符原样输出；
        // 扫描推进到内层 "{{host}}" 时它是合法变量 → 正常替换
        VariableResolutionResult result = _resolver.Resolve("a {{host b {{host}}", Environment);

        Assert.Equal("a {{host b http://localhost:8080", result.Text);
        Assert.False(result.HasUndefinedVariables);
    }

    [Fact]
    public void Resolve_变量名含非法字符_按字面量保留()
    {
        VariableResolutionResult result = _resolver.Resolve("{{ho st}}/api", Environment);

        Assert.Equal("{{ho st}}/api", result.Text);
        Assert.False(result.HasUndefinedVariables);
    }

    [Fact]
    public void Resolve_空变量名_按字面量保留()
    {
        VariableResolutionResult result = _resolver.Resolve("{{}}", Environment);

        Assert.Equal("{{}}", result.Text);
        Assert.False(result.HasUndefinedVariables);
    }

    [Fact]
    public void Resolve_变量名允许下划线连字符点_正常替换()
    {
        var environment = new HermesEnvironment
        {
            Id = "dev",
            Name = "开发环境",
            Variables = [new EnvironmentVariable("a_b-c.d", "ok")],
        };

        VariableResolutionResult result = _resolver.Resolve("{{a_b-c.d}}", environment);

        Assert.Equal("ok", result.Text);
    }

    [Fact]
    public void Resolve_空输入_返回空且无未定义()
    {
        VariableResolutionResult result = _resolver.Resolve("", Environment);

        Assert.Equal("", result.Text);
        Assert.False(result.HasUndefinedVariables);
    }
}
