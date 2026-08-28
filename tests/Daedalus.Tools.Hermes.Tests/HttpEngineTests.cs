using System.Net;

using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.Settings;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>HttpEngine 行为测试：以注入的 HttpMessageHandler 桩替代真实网络。</summary>
public sealed class HttpEngineTests
{
    private const string Base = "http://test.local";

    /// <summary>桩 handler 记录到的实际发送内容。</summary>
    private sealed record RecordedRequest(string Method, string Url, string? Body, IReadOnlyDictionary<string, string> Headers);

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IEnumerable<string>> h in request.Headers)
            {
                headers[h.Key] = string.Join(",", h.Value);
            }

            if (request.Content is not null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> h in request.Content.Headers)
                {
                    headers[h.Key] = string.Join(",", h.Value);
                }
            }

            // RequestUri 由引擎从合法 URL 构造，不可能为 null
            Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsoluteUri, body, headers));
            return await responder(request, cancellationToken);
        }
    }

    /// <summary>创建引擎 + 记录每次创建的桩 handler（每次 GetClient 未命中缓存各建一个）。</summary>
    private static (HttpEngine Engine, List<StubHandler> Handlers) CreateEngine(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        var handlers = new List<StubHandler>();
        var factory = new HttpClientFactory(() =>
        {
            var handler = new StubHandler(responder);
            handlers.Add(handler);
            return handler;
        });
        return (new HttpEngine(factory), handlers);
    }

    private static (HttpEngine Engine, List<StubHandler> Handlers) CreateEngine(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        CreateEngine((request, _) => Task.FromResult(responder(request)));

    private static HttpResponseMessage Respond(int status, string body = "", string? location = null, params (string Key, string Value)[] headers)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };
        if (location is not null)
        {
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        }

        foreach ((string key, string value) in headers)
        {
            response.Headers.TryAddWithoutValidation(key, value);
        }

        return response;
    }

    private static SendRequest Get(string url, RequestOptions? options = null) => new("GET", url, [], null, options);

    private static SendRequest Post(string url, string body, string contentType = "application/json", RequestOptions? options = null) =>
        new("POST", url, [], new RequestBody { Kind = RequestBodyKind.Raw, ContentType = contentType, Text = body }, options);

    [Fact]
    public async Task SendAsync_普通请求_返回单跳完整快照()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(200, "hello", null, ("X-Env", "dev")));
        SendRequest request = new("GET", $"{Base}/api", [new KeyValueEntry("X-Key", "k1")], null, null);

        SendResult result = await engine.SendAsync(request, HermesSettings.Default);

        ResponseHop hop = Assert.Single(result.Hops);
        Assert.Equal(1, hop.Index);
        Assert.Equal("GET", hop.Request.Method);
        Assert.Equal($"{Base}/api", hop.Request.Url);
        Assert.Contains(hop.Request.Headers, h => h.Key == "X-Key" && h.Value == "k1");
        Assert.Null(hop.Request.Body);
        Assert.Equal(200, hop.Response.Status);
        Assert.Equal("hello", hop.Response.Body);
        Assert.Contains(hop.Response.Headers, h => h.Key == "X-Env" && h.Value == "dev");
        Assert.True(hop.Response.ElapsedMs >= 0);
        Assert.False(result.RedirectLimitExceeded);
        Assert.False(result.RedirectLoopDetected);
        Assert.Same(hop, result.FinalHop);
        Assert.Single(handlers[0].Requests);
    }

    [Fact]
    public async Task SendAsync_全局不跟随_直接返回3xx单跳()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(302, location: "/next"));
        HermesSettings settings = HermesSettings.Default with { FollowRedirects = false };

        SendResult result = await engine.SendAsync(Get($"{Base}/a"), settings);

        ResponseHop hop = Assert.Single(result.Hops);
        Assert.Equal(302, hop.Response.Status);
        Assert.Single(handlers[0].Requests);
    }

    [Fact]
    public async Task SendAsync_请求强制不跟随_覆盖全局跟随()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(302, location: "/next"));

        SendResult result = await engine.SendAsync(Get($"{Base}/a", new RequestOptions(FollowRedirect: false, UseCookies: null)), HermesSettings.Default);

        Assert.Single(result.Hops);
        Assert.Single(handlers[0].Requests);
    }

    [Fact]
    public async Task SendAsync_请求强制跟随_覆盖全局不跟随()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(request =>
            request.RequestUri!.AbsolutePath == "/a" ? Respond(302, location: "/b") : Respond(200, "done"));
        HermesSettings settings = HermesSettings.Default with { FollowRedirects = false };

        SendResult result = await engine.SendAsync(Get($"{Base}/a", new RequestOptions(FollowRedirect: true, UseCookies: null)), settings);

        Assert.Equal(2, result.Hops.Count);
        Assert.Equal(200, result.FinalHop.Response.Status);
        Assert.Equal($"{Base}/b", handlers[0].Requests[1].Url);
    }

    [Fact]
    public async Task SendAsync_303重定向_改写GET并丢弃请求体()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(request =>
            request.RequestUri!.AbsolutePath == "/a" ? Respond(303, location: "/b") : Respond(200));

        SendResult result = await engine.SendAsync(Post($"{Base}/a", "{\"x\":1}"), HermesSettings.Default);

        Assert.Equal(2, result.Hops.Count);
        RecordedRequest second = handlers[0].Requests[1];
        Assert.Equal("GET", second.Method);
        Assert.Null(second.Body);
        Assert.Equal("GET", result.FinalHop.Request.Method);
        Assert.Null(result.FinalHop.Request.Body);
    }

    [Theory]
    [InlineData(307)]
    [InlineData(308)]
    public async Task SendAsync_307与308重定向_保持方法与请求体(int status)
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(request =>
            request.RequestUri!.AbsolutePath == "/a" ? Respond(status, location: "/b") : Respond(200));

        SendResult result = await engine.SendAsync(Post($"{Base}/a", "{\"x\":1}"), HermesSettings.Default);

        Assert.Equal(2, result.Hops.Count);
        RecordedRequest second = handlers[0].Requests[1];
        Assert.Equal("POST", second.Method);
        Assert.Equal("{\"x\":1}", second.Body);
        Assert.StartsWith("application/json", second.Headers["Content-Type"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{\"x\":1}", result.FinalHop.Request.Body);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    public async Task SendAsync_301与302对POST_按浏览器惯例改写GET(int status)
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(request =>
            request.RequestUri!.AbsolutePath == "/a" ? Respond(status, location: "/b") : Respond(200));

        SendResult result = await engine.SendAsync(Post($"{Base}/a", "payload"), HermesSettings.Default);

        Assert.Equal(2, result.Hops.Count);
        RecordedRequest second = handlers[0].Requests[1];
        Assert.Equal("GET", second.Method);
        Assert.Null(second.Body);
    }

    [Fact]
    public async Task SendAsync_302对GET_保持方法()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(request =>
            request.RequestUri!.AbsolutePath == "/a" ? Respond(302, location: "/b") : Respond(200));

        await engine.SendAsync(Get($"{Base}/a"), HermesSettings.Default);

        Assert.Equal("GET", handlers[0].Requests[1].Method);
    }

    [Fact]
    public async Task SendAsync_相对Location_相对上一跳解析()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(request => request.RequestUri!.AbsolutePath switch
        {
            "/dir/a" => Respond(302, location: "b"),
            "/dir/b" => Respond(302, location: "/root"),
            _ => Respond(200),
        });

        SendResult result = await engine.SendAsync(Get($"{Base}/dir/a"), HermesSettings.Default);

        Assert.Equal(3, result.Hops.Count);
        Assert.Equal($"{Base}/dir/b", handlers[0].Requests[1].Url);
        Assert.Equal($"{Base}/root", handlers[0].Requests[2].Url);
        Assert.Equal($"{Base}/root", result.FinalHop.Request.Url);
    }

    [Fact]
    public async Task SendAsync_跳转到已访问URL_环检测停止()
    {
        (HttpEngine engine, _) = CreateEngine(request =>
            request.RequestUri!.AbsolutePath == "/a" ? Respond(302, location: "/b") : Respond(302, location: "/a"));

        SendResult result = await engine.SendAsync(Get($"{Base}/a"), HermesSettings.Default);

        Assert.Equal(2, result.Hops.Count);
        Assert.True(result.RedirectLoopDetected);
        Assert.False(result.RedirectLimitExceeded);
    }

    [Fact]
    public async Task SendAsync_超过10跳_停止并标记超限()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(request =>
        {
            int index = int.Parse(request.RequestUri!.AbsolutePath.TrimStart('/')[1..], System.Globalization.CultureInfo.InvariantCulture);
            return Respond(302, location: $"/r{index + 1}");
        });

        SendResult result = await engine.SendAsync(Get($"{Base}/r0"), HermesSettings.Default);

        Assert.Equal(HttpEngine.MaxRedirectHops, result.Hops.Count);
        Assert.Equal(10, handlers[0].Requests.Count);
        Assert.True(result.RedirectLimitExceeded);
        Assert.False(result.RedirectLoopDetected);
        Assert.Equal(302, result.FinalHop.Response.Status);
    }

    [Fact]
    public async Task SendAsync_3xx无Location_按最终响应处理()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(302, "no-location"));

        SendResult result = await engine.SendAsync(Get($"{Base}/a"), HermesSettings.Default);

        ResponseHop hop = Assert.Single(result.Hops);
        Assert.Equal(302, hop.Response.Status);
        Assert.False(result.RedirectLimitExceeded);
        Assert.False(result.RedirectLoopDetected);
        Assert.Single(handlers[0].Requests);
    }

    [Fact]
    public async Task SendAsync_Cookie开关_选用对应的缓存client()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(200));

        await engine.SendAsync(Get($"{Base}/a"), HermesSettings.Default);
        await engine.SendAsync(Get($"{Base}/b", new RequestOptions(FollowRedirect: null, UseCookies: false)), HermesSettings.Default);
        await engine.SendAsync(Get($"{Base}/c"), HermesSettings.Default);

        Assert.Equal(2, handlers.Count);
        Assert.Equal(2, handlers[0].Requests.Count);
        Assert.Equal($"{Base}/b", Assert.Single(handlers[1].Requests).Url);
    }

    [Fact]
    public async Task SendAsync_取消_抛OperationCanceledException()
    {
        (HttpEngine engine, _) = CreateEngine(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Respond(200);
        });
        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.SendAsync(Get($"{Base}/a"), HermesSettings.Default, cts.Token));
    }

    [Fact]
    public async Task SendAsync_计时_每跳记录实际耗时()
    {
        (HttpEngine engine, _) = CreateEngine(async (_, _) =>
        {
            await Task.Delay(200);
            return Respond(200);
        });

        SendResult result = await engine.SendAsync(Get($"{Base}/a"), HermesSettings.Default);

        Assert.True(result.FinalHop.Response.ElapsedMs >= 150, $"实际耗时 {result.FinalHop.Response.ElapsedMs}ms，应不小于 150ms");
    }

    [Fact]
    public async Task SendAsync_禁用请求头_不发送()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(200));
        SendRequest request = new("GET", $"{Base}/a",
            [new KeyValueEntry("X-On", "1"), new KeyValueEntry("X-Off", "2", Enabled: false)], null, null);

        SendResult result = await engine.SendAsync(request, HermesSettings.Default);

        Assert.False(handlers[0].Requests[0].Headers.ContainsKey("X-Off"));
        Assert.Contains(result.FinalHop.Request.Headers, h => h.Key == "X-On");
        Assert.DoesNotContain(result.FinalHop.Request.Headers, h => h.Key == "X-Off");
    }

    [Fact]
    public async Task SendAsync_UrlEncoded请求体_编码启用字段并带表单ContentType()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(200));
        SendRequest request = new("POST", $"{Base}/a", [],
            new RequestBody
            {
                Kind = RequestBodyKind.UrlEncoded,
                Fields = [new KeyValueEntry("a", "1 2"), new KeyValueEntry("skip", "x", Enabled: false)],
            }, null);

        SendResult result = await engine.SendAsync(request, HermesSettings.Default);

        RecordedRequest sent = handlers[0].Requests[0];
        Assert.Equal("a=1%202", sent.Body);
        Assert.StartsWith("application/x-www-form-urlencoded", sent.Headers["Content-Type"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("a=1%202", result.FinalHop.Request.Body);
    }

    [Fact]
    public async Task SendAsync_自定义方法_原样发送()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(200));

        await engine.SendAsync(new SendRequest("PURGE", $"{Base}/a", [], null, null), HermesSettings.Default);

        Assert.Equal("PURGE", handlers[0].Requests[0].Method);
    }

    [Fact]
    public async Task SendAsync_显式ContentType头_覆盖请求体自带值不重复()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(200));
        SendRequest request = new("POST", $"{Base}/a",
            [new KeyValueEntry("Content-Type", "application/json;utf-8")],
            new RequestBody { Kind = RequestBodyKind.Raw, ContentType = "application/json", Text = "{}" }, null);

        SendResult result = await engine.SendAsync(request, HermesSettings.Default);

        // RecordedRequest 把同名头的多个值以逗号拼接：出现逗号即重复
        // （known header 枚举时会规范化为 "application/json; utf-8"，与 verbatim 值语义等价）
        Assert.Equal("application/json; utf-8", handlers[0].Requests[0].Headers["Content-Type"]);
        History.NameValuePair contentType = Assert.Single(
            result.FinalHop.Request.Headers, h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("application/json; utf-8", contentType.Value);
    }

    [Fact]
    public async Task SendAsync_显式ContentType头_覆盖UrlEncoded默认表单类型()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(200));
        SendRequest request = new("POST", $"{Base}/a",
            [new KeyValueEntry("Content-Type", "application/custom")],
            new RequestBody { Kind = RequestBodyKind.UrlEncoded, Fields = [new KeyValueEntry("a", "1")] }, null);

        await engine.SendAsync(request, HermesSettings.Default);

        Assert.Equal("application/custom", handlers[0].Requests[0].Headers["Content-Type"]);
    }

    [Fact]
    public async Task SendAsync_Connection头_原样发送()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(200));

        await engine.SendAsync(
            new SendRequest("GET", $"{Base}/a", [new KeyValueEntry("Connection", "keep-alive")], null, null),
            HermesSettings.Default);

        Assert.Equal("keep-alive", handlers[0].Requests[0].Headers["Connection"]);
    }

    [Fact]
    public async Task SendAsync_同名头_以最下方的值为准()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(200));
        SendRequest request = new("GET", $"{Base}/a",
            [new KeyValueEntry("X-Dup", "first"), new KeyValueEntry("x-dup", "second")], null, null);

        SendResult result = await engine.SendAsync(request, HermesSettings.Default);

        Assert.Equal("second", handlers[0].Requests[0].Headers["X-Dup"]);
        History.NameValuePair sent = Assert.Single(
            result.FinalHop.Request.Headers, h => h.Key.Equals("X-Dup", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("second", sent.Value);
    }

    [Fact]
    public async Task SendAsync_同名头最下方项被禁用_以上方启用项为准()
    {
        (HttpEngine engine, List<StubHandler> handlers) = CreateEngine(_ => Respond(200));
        SendRequest request = new("GET", $"{Base}/a",
            [new KeyValueEntry("X-Dup", "first"), new KeyValueEntry("X-Dup", "second", Enabled: false)], null, null);

        await engine.SendAsync(request, HermesSettings.Default);

        Assert.Equal("first", handlers[0].Requests[0].Headers["X-Dup"]);
    }
}
