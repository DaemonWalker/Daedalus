using System.Text.Json;

using Daedalus.Tools.Hermes.Variables;

namespace Daedalus.Tools.Hermes.Collections;

/// <summary>Postman 导入失败（不支持的版本 / 无法识别的结构 / 非法 JSON，FR-HERMES-032）。</summary>
public sealed class PostmanImportException : FormatException
{
    /// <param name="message">面向用户的失败原因。</param>
    public PostmanImportException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Postman 导入结果（hermes.md §9.1）：<see cref="Collection"/> 与 <see cref="Environment"/> 恰有一个非 null。
/// </summary>
/// <param name="Collection">导入的集合（Collection v2.1）。</param>
/// <param name="Environment">导入的环境（Environment v1）。</param>
/// <param name="IgnoredItems">被忽略内容的汇总说明（form-data、graphql、auth、prerequest 等），供导入后提示。</param>
public sealed record PostmanImportResult(
    HermesCollection? Collection,
    HermesEnvironment? Environment,
    IReadOnlyList<string> IgnoredItems);

/// <summary>
/// Postman 导入（hermes.md §9.1）：先嗅探结构判断 Collection v2.1 还是 Environment v1，
/// 其余版本/结构明确报错（FR-HERMES-032）。导入产物为新对象，名称冲突时自动追加序号，不覆盖已有数据。
/// </summary>
public sealed class PostmanImporter
{
    /// <summary>
    /// 导入 Postman 导出的 .json 文本。
    /// </summary>
    /// <param name="json">导出文件内容。</param>
    /// <param name="existingCollectionNames">已有集合名（用于名称冲突追加序号）；null 表示不去重。</param>
    /// <param name="existingEnvironmentNames">已有环境名（同上）。</param>
    /// <exception cref="PostmanImportException">JSON 非法、结构无法识别或版本不支持。</exception>
    public PostmanImportResult Import(
        string json,
        IReadOnlyCollection<string>? existingCollectionNames = null,
        IReadOnlyCollection<string>? existingEnvironmentNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new PostmanImportException($"不是合法的 JSON 文件：{ex.Message}");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new PostmanImportException("无法识别的 Postman 导出文件：根节点不是 JSON 对象。");
            }

            if (root.TryGetProperty("info", out JsonElement info) && info.ValueKind == JsonValueKind.Object
                && info.TryGetProperty("schema", out JsonElement schema))
            {
                return ImportCollection(root, schema.GetString() ?? string.Empty, existingCollectionNames);
            }

            if (root.TryGetProperty("values", out JsonElement values) && values.ValueKind == JsonValueKind.Array)
            {
                return ImportEnvironment(root, existingEnvironmentNames);
            }

            throw new PostmanImportException("无法识别的 Postman 导出文件：既不是 Collection v2.1 也不是 Environment v1。");
        }
    }

    // ---------- Collection v2.1 ----------

    private static PostmanImportResult ImportCollection(
        JsonElement root,
        string schema,
        IReadOnlyCollection<string>? existingNames)
    {
        // schema 形如 https://schema.getpostman.com/json/collection/v2.1.0/collection.json
        if (!schema.Contains("/collection/v2.1", StringComparison.Ordinal))
        {
            string version = schema.Contains("/collection/", StringComparison.Ordinal)
                ? schema.Split("/collection/")[1]
                : schema;
            throw new PostmanImportException($"不支持的 Postman Collection 版本（{version}），仅支持 v2.1。");
        }

        var ignored = new List<string>();
        string name = root.GetProperty("info").TryGetProperty("name", out JsonElement nameElement)
            ? nameElement.GetString() ?? string.Empty
            : string.Empty;
        if (name.Length == 0)
        {
            name = "未命名集合";
        }

        var items = new List<CollectionNode>();
        if (root.TryGetProperty("item", out JsonElement itemArray) && itemArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in itemArray.EnumerateArray())
            {
                if (MapItem(item, ignored) is { } node)
                {
                    items.Add(node);
                }
            }
        }

        // 集合级的 auth / variable / event 在内部模型中没有对应位置（hermes.md §9.1 忽略项）
        if (root.TryGetProperty("auth", out _))
        {
            ignored.Add($"集合「{name}」：auth 配置未导入");
        }

        if (root.TryGetProperty("variable", out _))
        {
            ignored.Add($"集合「{name}」：collection 变量未导入（可改为环境变量）");
        }

        if (root.TryGetProperty("event", out JsonElement events) && events.ValueKind == JsonValueKind.Array
            && events.EnumerateArray().Any())
        {
            ignored.Add($"集合「{name}」：集合级脚本（event）未导入");
        }

        var collection = new HermesCollection
        {
            Id = IdGenerator.NewId(),
            Name = DeduplicateName(name, existingNames),
            Items = items,
        };
        return new PostmanImportResult(collection, null, ignored);
    }

    private static CollectionNode? MapItem(JsonElement item, List<string> ignored)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string name = item.TryGetProperty("name", out JsonElement nameElement)
            ? nameElement.GetString() ?? "未命名"
            : "未命名";

        if (item.TryGetProperty("item", out JsonElement children) && children.ValueKind == JsonValueKind.Array)
        {
            var nodes = new List<CollectionNode>();
            foreach (JsonElement child in children.EnumerateArray())
            {
                if (MapItem(child, ignored) is { } node)
                {
                    nodes.Add(node);
                }
            }

            return new CollectionNode { Type = CollectionNodeType.Folder, Name = name, Items = nodes };
        }

        if (item.TryGetProperty("request", out JsonElement request))
        {
            return MapRequest(name, item, request, ignored);
        }

        ignored.Add($"条目「{name}」：既非文件夹也非请求，未导入");
        return null;
    }

    private static CollectionNode MapRequest(string name, JsonElement item, JsonElement request, List<string> ignored)
    {
        // v2.1 允许 request 退化为纯 URL 字符串
        if (request.ValueKind == JsonValueKind.String)
        {
            return new CollectionNode
            {
                Type = CollectionNodeType.Request,
                Name = name,
                Method = "GET",
                Url = request.GetString() ?? string.Empty,
            };
        }

        string method = request.TryGetProperty("method", out JsonElement methodElement)
            && methodElement.GetString() is { Length: > 0 } mappedMethod
            ? mappedMethod
            : "GET";

        string url = string.Empty;
        if (request.TryGetProperty("url", out JsonElement urlElement))
        {
            url = MapUrl(name, urlElement, ignored);
        }

        List<KeyValueEntry>? headers = null;
        if (request.TryGetProperty("header", out JsonElement headerArray) && headerArray.ValueKind == JsonValueKind.Array)
        {
            headers = [];
            foreach (JsonElement header in headerArray.EnumerateArray())
            {
                string key = header.TryGetProperty("key", out JsonElement keyElement) ? keyElement.GetString() ?? string.Empty : string.Empty;
                if (key.Length == 0)
                {
                    continue;
                }

                string value = header.TryGetProperty("value", out JsonElement valueElement) ? valueElement.GetString() ?? string.Empty : string.Empty;
                bool disabled = header.TryGetProperty("disabled", out JsonElement disabledElement) && disabledElement.ValueKind == JsonValueKind.True;
                headers.Add(new KeyValueEntry(key, value, !disabled));
            }
        }

        RequestBody? body = null;
        if (request.TryGetProperty("body", out JsonElement bodyElement) && bodyElement.ValueKind == JsonValueKind.Object)
        {
            body = MapBody(name, bodyElement, ignored);
        }

        string? script = null;
        // v2.1 中 event 挂在 item 上（与 request 平级），不是 request 的子节点
        if (item.TryGetProperty("event", out JsonElement eventArray) && eventArray.ValueKind == JsonValueKind.Array)
        {
            script = MapEvents(name, eventArray, ignored);
        }

        if (request.TryGetProperty("auth", out JsonElement auth) && auth.ValueKind != JsonValueKind.Null)
        {
            ignored.Add($"请求「{name}」：auth 配置未导入");
        }

        return new CollectionNode
        {
            Type = CollectionNodeType.Request,
            Name = name,
            Method = method,
            Url = url,
            Headers = headers,
            Body = body,
            PostResponseScript = script,
        };
    }

    private static string MapUrl(string requestName, JsonElement urlElement, List<string> ignored)
    {
        if (urlElement.ValueKind == JsonValueKind.String)
        {
            return urlElement.GetString() ?? string.Empty;
        }

        if (urlElement.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        // url.variable[] 是 Postman 的 :路径变量 定义；内部模型没有对应位置，:var 作为普通文本留在 URL 中（§9.1）
        if (urlElement.TryGetProperty("variable", out JsonElement variables) && variables.ValueKind == JsonValueKind.Array
            && variables.EnumerateArray().Any())
        {
            ignored.Add($"请求「{requestName}」：URL 路径变量（:变量）未定义值，已按普通文本保留在 URL 中");
        }

        if (urlElement.TryGetProperty("raw", out JsonElement raw) && raw.GetString() is { } rawUrl)
        {
            return rawUrl;
        }

        // 兜底：无 raw 时由 protocol/host/path 重建（真实导出文件几乎都带 raw）
        string protocol = urlElement.TryGetProperty("protocol", out JsonElement protocolElement)
            ? protocolElement.GetString() ?? "http"
            : "http";
        string host = urlElement.TryGetProperty("host", out JsonElement hostArray) && hostArray.ValueKind == JsonValueKind.Array
            ? string.Join('.', hostArray.EnumerateArray().Select(h => h.GetString()))
            : string.Empty;
        string path = urlElement.TryGetProperty("path", out JsonElement pathArray) && pathArray.ValueKind == JsonValueKind.Array
            ? string.Join('/', pathArray.EnumerateArray().Select(p => p.GetString()))
            : string.Empty;
        return $"{protocol}://{host}/{path}";
    }

    private static RequestBody? MapBody(string requestName, JsonElement body, List<string> ignored)
    {
        string mode = body.TryGetProperty("mode", out JsonElement modeElement) ? modeElement.GetString() ?? string.Empty : string.Empty;
        switch (mode)
        {
            case "raw":
                string? contentType = null;
                if (body.TryGetProperty("options", out JsonElement options)
                    && options.TryGetProperty("raw", out JsonElement rawOptions)
                    && rawOptions.TryGetProperty("language", out JsonElement language))
                {
                    contentType = language.GetString() switch
                    {
                        "json" => "application/json",
                        "xml" => "application/xml",
                        "html" => "text/html",
                        "text" => "text/plain",
                        "javascript" => "application/javascript",
                        _ => null,
                    };
                }

                return new RequestBody
                {
                    Kind = RequestBodyKind.Raw,
                    ContentType = contentType,
                    Text = body.TryGetProperty("raw", out JsonElement rawText) ? rawText.GetString() ?? string.Empty : string.Empty,
                };
            case "urlencoded":
                var fields = new List<KeyValueEntry>();
                if (body.TryGetProperty("urlencoded", out JsonElement fieldArray) && fieldArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement field in fieldArray.EnumerateArray())
                    {
                        string key = field.TryGetProperty("key", out JsonElement keyElement) ? keyElement.GetString() ?? string.Empty : string.Empty;
                        if (key.Length == 0)
                        {
                            continue;
                        }

                        string value = field.TryGetProperty("value", out JsonElement valueElement) ? valueElement.GetString() ?? string.Empty : string.Empty;
                        bool disabled = field.TryGetProperty("disabled", out JsonElement disabledElement) && disabledElement.ValueKind == JsonValueKind.True;
                        fields.Add(new KeyValueEntry(key, value, !disabled));
                    }
                }

                return new RequestBody { Kind = RequestBodyKind.UrlEncoded, Fields = fields };
            case "formdata":
                ignored.Add($"请求「{requestName}」：form-data 请求体未导入（本期仅支持 raw / urlencoded）");
                return null;
            case "graphql":
                ignored.Add($"请求「{requestName}」：GraphQL 请求体未导入");
                return null;
            case "file":
                ignored.Add($"请求「{requestName}」：文件请求体未导入");
                return null;
            default:
                return null;
        }
    }

    private static string? MapEvents(string requestName, JsonElement eventArray, List<string> ignored)
    {
        string? script = null;
        foreach (JsonElement eventEntry in eventArray.EnumerateArray())
        {
            string listen = eventEntry.TryGetProperty("listen", out JsonElement listenElement) ? listenElement.GetString() ?? string.Empty : string.Empty;
            if (listen == "test" && eventEntry.TryGetProperty("script", out JsonElement scriptElement)
                && scriptElement.TryGetProperty("exec", out JsonElement exec))
            {
                // exec 为字符串数组（v2.1）或单个字符串（旧式），多行以换行连接（§9.1）
                script = exec.ValueKind == JsonValueKind.Array
                    ? string.Join('\n', exec.EnumerateArray().Select(line => line.GetString()))
                    : exec.GetString();
            }
            else if (listen == "prerequest")
            {
                ignored.Add($"请求「{requestName}」：前置脚本（prerequest）未导入（本期仅支持后事件脚本）");
            }
        }

        return script;
    }

    // ---------- Environment v1 ----------

    private static PostmanImportResult ImportEnvironment(JsonElement root, IReadOnlyCollection<string>? existingNames)
    {
        var ignored = new List<string>();
        string name = root.TryGetProperty("name", out JsonElement nameElement) && nameElement.GetString() is { Length: > 0 } envName
            ? envName
            : "未命名环境";

        var variables = new List<EnvironmentVariable>();
        foreach (JsonElement value in root.GetProperty("values").EnumerateArray())
        {
            string key = value.TryGetProperty("key", out JsonElement keyElement) ? keyElement.GetString() ?? string.Empty : string.Empty;
            if (key.Length == 0)
            {
                continue;
            }

            string variableValue = value.TryGetProperty("value", out JsonElement valueElement)
                ? valueElement.ValueKind == JsonValueKind.String ? valueElement.GetString() ?? string.Empty : valueElement.ToString()
                : string.Empty;
            bool enabled = !value.TryGetProperty("enabled", out JsonElement enabledElement) || enabledElement.ValueKind != JsonValueKind.False;
            bool secret = value.TryGetProperty("type", out JsonElement typeElement) && typeElement.GetString() == "secret";
            variables.Add(new EnvironmentVariable(key, variableValue, secret, enabled));
        }

        var environment = new HermesEnvironment
        {
            Id = IdGenerator.NewId(),
            Name = DeduplicateName(name, existingNames),
            Variables = variables,
        };
        return new PostmanImportResult(null, environment, ignored);
    }

    // ---------- 共用 ----------

    /// <summary>名称冲突时追加序号：「名称」「名称 (2)」「名称 (3)」……（hermes.md §9.1）。</summary>
    internal static string DeduplicateName(string name, IReadOnlyCollection<string>? existingNames)
    {
        if (existingNames is null || !existingNames.Contains(name, StringComparer.Ordinal))
        {
            return name;
        }

        for (int index = 2; ; index++)
        {
            string candidate = $"{name} ({index})";
            if (!existingNames.Contains(candidate, StringComparer.Ordinal))
            {
                return candidate;
            }
        }
    }
}
