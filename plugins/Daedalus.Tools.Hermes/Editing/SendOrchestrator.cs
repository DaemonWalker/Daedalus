using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.History;
using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.Settings;
using Daedalus.Tools.Hermes.Variables;

using Serilog;

namespace Daedalus.Tools.Hermes.Editing;

/// <summary>变量替换完成、待发送的请求。</summary>
/// <param name="Request">引擎发送输入（变量替换后的最终内容 + 请求级选项）。</param>
/// <param name="UndefinedVariables">本次替换中未定义的变量清单（跨 URL/请求头/请求体去重），供状态栏警告（FR-HERMES-022）。</param>
public sealed record PreparedRequest(SendRequest Request, IReadOnlyList<string> UndefinedVariables);

/// <summary>
/// 发送编排（hermes.md §5.1 的界面侧部分）：草稿 → 变量替换 → HttpEngine 发送 → 历史记录组装。
/// 不碰 UI，历史落盘由调用方（面板）经 HistoryStore 异步执行。
/// </summary>
/// <param name="engine">HTTP 引擎。</param>
/// <param name="logger">插件日志器；为 null 时不写日志（主要用于测试）。</param>
public sealed class SendOrchestrator(HttpEngine engine, ILogger? logger = null)
{
    private readonly VariableResolver _resolver = new();

    /// <summary>
    /// 变量替换并组装引擎输入：作用于 URL、请求头的值、请求体文本与 urlencoded 字段的键值（hermes.md §6）。
    /// 未定义变量原样保留并汇总去重。
    /// </summary>
    public PreparedRequest Prepare(RequestDraft draft, HermesEnvironment? environment)
    {
        ArgumentNullException.ThrowIfNull(draft);
        logger?.Debug("变量替换开始：{Method} {Url}", draft.Method, draft.Url);
        var undefined = new List<string>();

        string url = Resolve(draft.Url, environment, undefined);
        List<KeyValueEntry> headers = [.. draft.Headers.Select(h =>
            h with { Value = Resolve(h.Value, environment, undefined) })];
        RequestBody? body = ResolveBody(draft.Body, environment, undefined);

        // Debug 记录替换结果：环境取值是否生效（尤其未定义变量数）是发送问题的常见排查入口
        logger?.Debug("变量替换完成：{ResolvedUrl}，未定义变量 {UndefinedCount} 个", url, undefined.Count);
        return new PreparedRequest(new SendRequest(draft.Method, url, headers, body, draft.Options), undefined);
    }

    /// <summary>发送（不阻塞 UI，支持取消，FR-HERMES-005）。</summary>
    public Task<SendResult> SendAsync(PreparedRequest prepared, HermesSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        return engine.SendAsync(prepared.Request, settings, cancellationToken);
    }

    /// <summary>
    /// 组装历史记录（hermes.md §11.3）：请求取变量替换后的发送内容（仅生效头），
    /// 响应取最终一跳；RedirectHops 为实际跟随的跳数（链长 - 1）。
    /// </summary>
    public HistoryEntry BuildHistoryEntry(PreparedRequest prepared, SendResult result, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(result);

        SendRequest request = prepared.Request;
        ResponseHop final = result.FinalHop;
        return new HistoryEntry
        {
            Id = IdGenerator.NewId(),
            Timestamp = timestamp,
            Request = new HistoryRequest(
                request.Method,
                request.Url,
                [.. request.Headers.Where(h => h.Enabled).Select(h => new NameValuePair(h.Key, h.Value))],
                HttpEngine.GetBodyText(request.Body)),
            Response = new HistoryResponse(
                final.Response.Status,
                final.Response.ElapsedMs,
                [.. final.Response.Headers],
                final.Response.Body,
                BodyTruncated: false),
            RedirectHops = result.Hops.Count - 1,
        };
    }

    private RequestBody? ResolveBody(RequestBody? body, HermesEnvironment? environment, List<string> undefined)
    {
        if (body is null)
        {
            return null;
        }

        if (body.Kind == RequestBodyKind.UrlEncoded)
        {
            return body with
            {
                Fields = [.. (body.Fields ?? []).Select(f => f with
                {
                    Key = Resolve(f.Key, environment, undefined),
                    Value = Resolve(f.Value, environment, undefined),
                })],
            };
        }

        return body with { Text = body.Text is null ? null : Resolve(body.Text, environment, undefined) };
    }

    private string Resolve(string input, HermesEnvironment? environment, List<string> undefined)
    {
        VariableResolutionResult result = _resolver.Resolve(input, environment);
        foreach (string name in result.UndefinedVariables)
        {
            if (!undefined.Contains(name, StringComparer.Ordinal))
            {
                undefined.Add(name);
            }
        }

        return result.Text;
    }
}
