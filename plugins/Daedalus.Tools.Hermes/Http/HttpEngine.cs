using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.History;
using Daedalus.Tools.Hermes.Settings;

using Serilog;

namespace Daedalus.Tools.Hermes.Http;

/// <summary>
/// HTTP 发送引擎（hermes.md §5）：异步发送、取消、逐跳计时、重定向手动跟随并收集跳转链。
/// 不持有设置，每次发送经参数传入全局设置，与请求级覆盖合成生效值。
/// </summary>
/// <param name="clientFactory">按生效 Cookie 设置提供 client 的工厂。</param>
/// <param name="logger">插件日志器；为 null 时不写日志（主要用于测试）。</param>
public sealed class HttpEngine(HttpClientFactory clientFactory, ILogger? logger = null)
{
    /// <summary>重定向跟随上限（跳数，FR-HERMES-006 固定 10 跳）。</summary>
    public const int MaxRedirectHops = 10;

    /// <summary>
    /// 发送请求并按需跟随重定向，返回完整跳转链。
    /// 取消时抛 <see cref="OperationCanceledException"/>（FR-HERMES-005）。
    /// </summary>
    /// <param name="request">变量替换完成后的请求。</param>
    /// <param name="settings">全局设置，与请求级覆盖合成生效值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<SendResult> SendAsync(SendRequest request, HermesSettings settings, CancellationToken cancellationToken = default)
    {
        bool followRedirects = request.Options?.FollowRedirect ?? settings.FollowRedirects;
        bool useCookies = request.Options?.UseCookies ?? settings.UseCookies;
        HttpClient client = clientFactory.GetClient(useCookies);

        var hops = new List<ResponseHop>();
        // 环检测按"完全相同的 URL"（hermes.md §5.3），不做大小写折叠，避免误判大小写敏感路径
        var visitedUrls = new HashSet<string>(StringComparer.Ordinal);
        bool redirectLimitExceeded = false;
        bool redirectLoopDetected = false;

        string method = request.Method;
        string url = request.Url;
        RequestBody? body = request.Body;
        visitedUrls.Add(url);

        while (true)
        {
            string? bodyText;
            using (HttpRequestMessage message = BuildRequestMessage(method, url, request.Headers, body, out bodyText))
            {
                var stopwatch = Stopwatch.StartNew();
                using HttpResponseMessage response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                string responseBody = await ResponseBodyDecoder.DecodeAsync(response.Content, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                hops.Add(new ResponseHop(
                    hops.Count + 1,
                    SnapshotRequest(method, url, message, bodyText),
                    SnapshotResponse(response, responseBody, stopwatch.ElapsedMilliseconds)));

                // 逐跳 Debug：定位重定向链问题时对照界面"跳转链"页（hermes.md §5.3）
                logger?.Debug("HTTP 第 {Hop} 跳：{Method} {Url} → {Status}，耗时 {ElapsedMs} ms",
                    hops.Count, method, url, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);

                if (!followRedirects || !IsRedirect(response.StatusCode))
                {
                    break;
                }

                Uri? location = response.Headers.Location;
                if (location is null)
                {
                    // 3xx 但无 Location 头，无法跟随，按最终响应处理
                    break;
                }

                // Location 可为相对 URL，相对上一跳解析（hermes.md §5.3）
                string nextUrl = new Uri(new Uri(url), location).AbsoluteUri;
                if (!visitedUrls.Add(nextUrl))
                {
                    redirectLoopDetected = true;
                    break;
                }

                if (hops.Count >= MaxRedirectHops)
                {
                    redirectLimitExceeded = true;
                    break;
                }

                (method, body) = RewriteMethod(response.StatusCode, method, body);
                url = nextUrl;
            }
        }

        return new SendResult(hops, redirectLimitExceeded, redirectLoopDetected);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or      // 301
        HttpStatusCode.Found or                 // 302
        HttpStatusCode.SeeOther or              // 303
        HttpStatusCode.TemporaryRedirect or     // 307
        HttpStatusCode.PermanentRedirect;       // 308

    private static (string Method, RequestBody? Body) RewriteMethod(HttpStatusCode statusCode, string method, RequestBody? body)
    {
        // 方法改写规则（hermes.md §5.3）：303 一律改 GET 丢体；301/302 仅对 POST 按浏览器惯例改 GET；307/308 原样重发
        if (statusCode == HttpStatusCode.SeeOther)
        {
            return ("GET", null);
        }

        bool redirectChangesPost = statusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found;
        if (redirectChangesPost && string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return ("GET", null);
        }

        return (method, body);
    }

    private static HttpRequestMessage BuildRequestMessage(
        string method, string url, IReadOnlyList<KeyValueEntry> headers, RequestBody? body, out string? bodyText)
    {
        var message = new HttpRequestMessage(new HttpMethod(method), url);
        message.Content = BuildContent(body, out bodyText);

        // 同名键以最下方（最后出现）的启用项为准：先找出每个键的生效位置，非生效位置的同名项跳过
        var effectiveIndexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
        {
            if (headers[i].Enabled)
            {
                effectiveIndexByKey[headers[i].Key] = i;
            }
        }

        for (int i = 0; i < headers.Count; i++)
        {
            KeyValueEntry header = headers[i];
            if (!header.Enabled || effectiveIndexByKey[header.Key] != i)
            {
                continue;
            }

            // 显式 Content-Type 头覆盖请求体自带值（StringContent 默认 text/plain 或 body.ContentType）：
            // 先移除再添加，否则同名单值头会出现两个值（如 "application/json;utf-8, application/json"）
            if (message.Content is not null && header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                message.Content.Headers.Remove("Content-Type");
            }

            // 内容头（如 Content-Type）按 HTTP 规范只能挂在 Content 上，请求头添加失败时落到内容头
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                message.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return message;
    }

    private static HttpContent? BuildContent(RequestBody? body, out string? bodyText)
    {
        bodyText = GetBodyText(body);
        if (body is null)
        {
            return null;
        }

        // body 非 null 时 GetBodyText 保证非 null（raw 空文本返回 string.Empty）
        if (body.Kind == RequestBodyKind.UrlEncoded)
        {
            return new StringContent(bodyText!, Encoding.UTF8, "application/x-www-form-urlencoded");
        }

        // Raw：未指定 Content-Type 时保留 StringContent 默认的 text/plain; charset=utf-8
        var content = new StringContent(bodyText ?? string.Empty, Encoding.UTF8);
        if (!string.IsNullOrWhiteSpace(body.ContentType)
            && MediaTypeHeaderValue.TryParse(body.ContentType, out MediaTypeHeaderValue? mediaType))
        {
            content.Headers.ContentType = mediaType;
        }

        return content;
    }

    // 请求体序列化为文本的口径与发送一致，供历史记录组装（SendOrchestrator）复用
    internal static string? GetBodyText(RequestBody? body)
    {
        if (body is null)
        {
            return null;
        }

        if (body.Kind == RequestBodyKind.UrlEncoded)
        {
            IEnumerable<KeyValueEntry> fields = (body.Fields ?? []).Where(f => f.Enabled);
            return string.Join("&", fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
        }

        return body.Text ?? string.Empty;
    }

    private static HopRequest SnapshotRequest(string method, string url, HttpRequestMessage message, string? bodyText)
    {
        List<NameValuePair> headers = [.. message.Headers.SelectMany(h => h.Value.Select(v => new NameValuePair(h.Key, v)))];
        if (message.Content is not null)
        {
            headers.AddRange(message.Content.Headers.SelectMany(h => h.Value.Select(v => new NameValuePair(h.Key, v))));
        }

        return new HopRequest(method, url, headers, bodyText);
    }

    private static HopResponse SnapshotResponse(HttpResponseMessage response, string body, long elapsedMs)
    {
        List<NameValuePair> headers = [.. response.Headers.SelectMany(h => h.Value.Select(v => new NameValuePair(h.Key, v)))];
        headers.AddRange(response.Content.Headers.SelectMany(h => h.Value.Select(v => new NameValuePair(h.Key, v))));
        return new HopResponse((int)response.StatusCode, response.ReasonPhrase, headers, body, elapsedMs);
    }
}
