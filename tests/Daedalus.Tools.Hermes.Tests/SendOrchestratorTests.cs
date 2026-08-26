using System.Net;

using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Editing;
using Daedalus.Tools.Hermes.History;
using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.Settings;
using Daedalus.Tools.Hermes.Variables;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>SendOrchestrator：变量替换组装、未定义汇总、历史记录组装（hermes.md §5.1）。</summary>
public sealed class SendOrchestratorTests
{
    private static readonly HermesEnvironment Environment = new()
    {
        Id = "dev",
        Name = "开发环境",
        Variables = [new EnvironmentVariable("host", "http://dev.local"), new EnvironmentVariable("token", "abc")],
    };

    /// <summary>固定 200 响应的桩引擎。</summary>
    private static HttpEngine CreateEngine() =>
        new(new HttpClientFactory(() => new Stub200Handler()));

    private sealed class Stub200Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
    }

    [Fact]
    public void Prepare_URL头体中的变量_全部替换()
    {
        var orchestrator = new SendOrchestrator(CreateEngine());
        var draft = new RequestDraft
        {
            Method = "POST",
            Url = "{{host}}/api/login?t={{token}}",
            Headers = [new KeyValueEntry("Authorization", "Bearer {{token}}")],
            Body = new RequestBody { Kind = RequestBodyKind.Raw, ContentType = "text/plain", Text = "t={{token}}" },
        };

        PreparedRequest prepared = orchestrator.Prepare(draft, Environment);

        Assert.Equal("http://dev.local/api/login?t=abc", prepared.Request.Url);
        Assert.Equal("Bearer abc", prepared.Request.Headers[0].Value);
        Assert.Equal("t=abc", prepared.Request.Body?.Text);
        Assert.Empty(prepared.UndefinedVariables);
    }

    [Fact]
    public void Prepare_urlencoded字段_键值均替换()
    {
        var orchestrator = new SendOrchestrator(CreateEngine());
        var draft = new RequestDraft
        {
            Url = "{{host}}/f",
            Body = new RequestBody
            {
                Kind = RequestBodyKind.UrlEncoded,
                Fields = [new KeyValueEntry("k{{token}}", "v{{host}}")],
            },
        };

        PreparedRequest prepared = orchestrator.Prepare(draft, Environment);

        Assert.Equal("kabc", prepared.Request.Body?.Fields?[0].Key);
        Assert.Equal("vhttp://dev.local", prepared.Request.Body?.Fields?[0].Value);
    }

    [Fact]
    public void Prepare_未定义变量_原样保留并跨部位去重汇总()
    {
        var orchestrator = new SendOrchestrator(CreateEngine());
        var draft = new RequestDraft
        {
            Url = "{{missing}}/x",
            Headers = [new KeyValueEntry("A", "{{missing}}"), new KeyValueEntry("B", "{{other}}")],
            Body = new RequestBody { Kind = RequestBodyKind.Raw, Text = "{{missing}}" },
        };

        PreparedRequest prepared = orchestrator.Prepare(draft, Environment);

        Assert.Equal("{{missing}}/x", prepared.Request.Url);
        Assert.Equal(["missing", "other"], prepared.UndefinedVariables);
    }

    [Fact]
    public void Prepare_无启用环境_全部变量视为未定义()
    {
        var orchestrator = new SendOrchestrator(CreateEngine());
        var draft = new RequestDraft { Url = "{{host}}/x" };

        PreparedRequest prepared = orchestrator.Prepare(draft, environment: null);

        Assert.Equal("{{host}}/x", prepared.Request.Url);
        Assert.Equal(["host"], prepared.UndefinedVariables);
    }

    [Fact]
    public void Prepare_请求级选项_原样传递给引擎输入()
    {
        var orchestrator = new SendOrchestrator(CreateEngine());
        var draft = new RequestDraft { Url = "http://a/", Options = new RequestOptions(false, false) };

        PreparedRequest prepared = orchestrator.Prepare(draft, null);

        Assert.Equal(new RequestOptions(false, false), prepared.Request.Options);
    }

    [Fact]
    public async Task SendAsync_委托引擎发送_返回跳转链()
    {
        var orchestrator = new SendOrchestrator(CreateEngine());
        PreparedRequest prepared = orchestrator.Prepare(new RequestDraft { Url = "http://a/" }, null);

        SendResult result = await orchestrator.SendAsync(prepared, HermesSettings.Default);

        Assert.Single(result.Hops);
        Assert.Equal(200, result.FinalHop.Response.Status);
    }

    [Fact]
    public async Task BuildHistoryEntry_取最终一跳_仅生效头_记录跳数()
    {
        var orchestrator = new SendOrchestrator(CreateEngine());
        var draft = new RequestDraft
        {
            Method = "POST",
            Url = "http://a/login",
            Headers = [new KeyValueEntry("X-On", "1"), new KeyValueEntry("X-Off", "2", Enabled: false)],
            Body = new RequestBody { Kind = RequestBodyKind.Raw, Text = "body-text" },
        };
        PreparedRequest prepared = orchestrator.Prepare(draft, null);
        SendResult result = await orchestrator.SendAsync(prepared, HermesSettings.Default);
        var timestamp = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.FromHours(8));

        HistoryEntry entry = orchestrator.BuildHistoryEntry(prepared, result, timestamp);

        Assert.Equal(timestamp, entry.Timestamp);
        Assert.Equal("POST", entry.Request.Method);
        Assert.Equal("http://a/login", entry.Request.Url);
        Assert.Equal([new NameValuePair("X-On", "1")], entry.Request.Headers);
        Assert.Equal("body-text", entry.Request.Body);
        Assert.Equal(200, entry.Response.Status);
        Assert.Equal("ok", entry.Response.Body);
        Assert.False(entry.Response.BodyTruncated);
        Assert.Equal(0, entry.RedirectHops);
    }

    [Fact]
    public void BuildHistoryEntry_urlencoded体_按发送口径序列化()
    {
        var orchestrator = new SendOrchestrator(CreateEngine());
        var draft = new RequestDraft
        {
            Url = "http://a/f",
            Body = new RequestBody
            {
                Kind = RequestBodyKind.UrlEncoded,
                Fields = [new KeyValueEntry("a", "1"), new KeyValueEntry("b", "2", Enabled: false)],
            },
        };
        PreparedRequest prepared = orchestrator.Prepare(draft, null);
        var result = new SendResult(
            [new ResponseHop(1,
                new HopRequest("POST", "http://a/f", [], null),
                new HopResponse(200, "OK", [], "ok", 5))],
            RedirectLimitExceeded: false,
            RedirectLoopDetected: false);

        HistoryEntry entry = orchestrator.BuildHistoryEntry(prepared, result, DateTimeOffset.Now);

        Assert.Equal("a=1", entry.Request.Body);
        Assert.Equal(0, entry.RedirectHops);
    }
}
