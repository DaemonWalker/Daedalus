using System.Text;

using Daedalus.Tools.Hermes.Editing;

namespace Daedalus.Tools.Hermes.Collections;

/// <summary>
/// cURL 导入结果（hermes.md §9.2）。
/// </summary>
/// <param name="Draft">导入的请求草稿（加载到当前编辑区，不自动入集合，FR-HERMES-034）。</param>
/// <param name="IgnoredArguments">被忽略的参数汇总（未知参数 / 多余位置参数），供导入后提示。</param>
/// <param name="HasInsecureFlag">命令含 -k/--insecure；不映射为请求属性，提示用户可开启全局"忽略证书校验"。</param>
public sealed record CurlImportResult(
    RequestDraft Draft,
    IReadOnlyList<string> IgnoredArguments,
    bool HasInsecureFlag);

/// <summary>
/// cURL 导入（hermes.md §9.2）：Chrome DevTools "Copy as cURL (bash)" 文本 → 请求草稿。
/// 先做 bash 分词（单双引号、\ 转义、行尾 \ 续行），再按参数表映射；不认识的参数忽略并汇总。
/// </summary>
public sealed class CurlImporter
{
    /// <summary>解析 cURL 命令文本。</summary>
    /// <exception cref="FormatException">文本为空或不含任何有效参数。</exception>
    public CurlImportResult Import(string commandText)
    {
        ArgumentNullException.ThrowIfNull(commandText);
        if (string.IsNullOrWhiteSpace(commandText))
        {
            throw new FormatException("cURL 命令为空。");
        }

        List<string> tokens = Tokenize(commandText);
        // 允许省略 curl 前缀（只粘贴参数部分）
        if (tokens.Count > 0 && tokens[0].Equals("curl", StringComparison.OrdinalIgnoreCase))
        {
            tokens.RemoveAt(0);
        }

        if (tokens.Count == 0)
        {
            throw new FormatException("cURL 命令为空。");
        }

        string? method = null;
        string? url = null;
        var headers = new List<KeyValueEntry>();
        var dataParts = new List<string>();
        string? cookie = null;
        string? user = null;
        string? userAgent = null;
        var ignored = new List<string>();
        bool insecure = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];

            // --name=value 形式拆出内联值；短参数（-XPOST 连写）按前两字符匹配
            string? inlineValue = null;
            string optionName = token;
            if (token.StartsWith("--", StringComparison.Ordinal) && token.IndexOf('=', StringComparison.Ordinal) is > 2 and int equalsIndex)
            {
                optionName = token[..equalsIndex];
                inlineValue = token[(equalsIndex + 1)..];
            }
            else if (token.Length > 2 && token[0] == '-' && token[1] != '-')
            {
                optionName = token[..2];
            }

            switch (optionName)
            {
                case "-X":
                case "--request":
                    method = TakeValue(optionName, "X");
                    break;
                case "--url":
                    url = TakeValue(optionName, null);
                    break;
                case "-H":
                case "--header":
                    AddHeader(TakeValue(optionName, "H"));
                    break;
                case "-d":
                case "--data":
                case "--data-raw":
                case "--data-ascii":
                case "--data-binary":
                    dataParts.Add(TakeValue(optionName, "d"));
                    break;
                case "-b":
                case "--cookie":
                    // 与 Cookie: 头汇入同一字段（§9.2）
                    string cookieValue = TakeValue(optionName, "b");
                    cookie = cookie is null ? cookieValue : cookie + "; " + cookieValue;
                    break;
                case "-u":
                case "--user":
                    user = TakeValue(optionName, "u");
                    break;
                case "-A":
                case "--user-agent":
                    userAgent = TakeValue(optionName, "A");
                    break;
                case "-k":
                case "--insecure":
                    insecure = true;
                    break;
                default:
                    if (!token.StartsWith('-'))
                    {
                        if (url is null)
                        {
                            // 裸 URL 参数（§9.2）
                            url = token;
                        }
                        else
                        {
                            ignored.Add($"多余的位置参数：{token}");
                        }
                    }
                    else
                    {
                        ignored.Add($"未知参数：{token}");
                    }

                    break;
            }

            // 取参数值：内联 > 短参数连写 > 下一个 token
            string TakeValue(string name, string? shortName)
            {
                if (inlineValue is not null)
                {
                    return inlineValue;
                }

                if (shortName is not null
                    && token.StartsWith('-' + shortName, StringComparison.Ordinal)
                    && !token.StartsWith("--", StringComparison.Ordinal)
                    && token.Length > shortName.Length + 1)
                {
                    return token[(shortName.Length + 1)..];
                }

                if (i + 1 < tokens.Count)
                {
                    return tokens[++i];
                }

                throw new FormatException($"参数 {name} 缺少值。");
            }

            void AddHeader(string header)
            {
                int colon = header.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0)
                {
                    ignored.Add($"无法解析的请求头：{header}");
                    return;
                }

                string key = header[..colon].Trim();
                string value = header[(colon + 1)..].Trim();
                if (key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    // Cookie 头与 -b 参数汇入同一字段（§9.2）
                    cookie = cookie is null ? value : cookie + "; " + value;
                    return;
                }

                headers.Add(new KeyValueEntry(key, value));
            }
        }

        if (user is not null)
        {
            string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(user));
            headers.Add(new KeyValueEntry("Authorization", $"Basic {credentials}"));
        }

        if (userAgent is not null)
        {
            headers.Add(new KeyValueEntry("User-Agent", userAgent));
        }

        if (cookie is not null)
        {
            headers.Add(new KeyValueEntry("Cookie", cookie));
        }

        if (url is null)
        {
            throw new FormatException("cURL 命令中找不到 URL。");
        }

        RequestBody? body = null;
        if (dataParts.Count > 0)
        {
            // 多个 --data 以 & 连接（§9.2）；Content-Type 缺省 form-urlencoded，已有 Content-Type 头时沿用
            string? contentType = headers
                .FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value
                ?? "application/x-www-form-urlencoded";
            body = new RequestBody { Kind = RequestBodyKind.Raw, ContentType = contentType, Text = string.Join('&', dataParts) };
        }

        var draft = new RequestDraft
        {
            // curl 语义：带数据且未显式指定方法时默认为 POST
            Method = method ?? (dataParts.Count > 0 ? "POST" : "GET"),
            Url = url,
            Headers = headers,
            Body = body,
        };
        return new CurlImportResult(draft, ignored, insecure);
    }

    /// <summary>
    /// bash 分词：单引号内全字面量；双引号内 \" \\ \$ \` 转义；引号外 \ 转义任意字符；
    /// 行尾 \ 为续行；空白分隔 token。
    /// </summary>
    internal static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inToken = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!inToken && char.IsWhiteSpace(c))
            {
                continue;
            }

            switch (c)
            {
                case '\\':
                    if (i + 1 < text.Length && (text[i + 1] is '\n' || text[i + 1] is '\r'))
                    {
                        // 行尾续行：吞掉 \ 与整个换行（\n 或 \r\n）；不构成 token 边界，空 token 不产出
                        i += text[i + 1] == '\r' && i + 2 < text.Length && text[i + 2] == '\n' ? 2 : 1;
                        break;
                    }

                    if (i + 1 < text.Length)
                    {
                        current.Append(text[++i]);
                    }

                    inToken = true;
                    break;
                case '\'':
                    inToken = true;
                    while (i + 1 < text.Length && text[++i] != '\'')
                    {
                        current.Append(text[i]);
                    }

                    break;
                case '"':
                    inToken = true;
                    while (i + 1 < text.Length && text[++i] != '"')
                    {
                        if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\' or '$' or '`')
                        {
                            current.Append(text[++i]);
                        }
                        else
                        {
                            current.Append(text[i]);
                        }
                    }

                    break;
                default:
                    if (char.IsWhiteSpace(c))
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        inToken = false;
                    }
                    else
                    {
                        current.Append(c);
                        inToken = true;
                    }

                    break;
            }
        }

        if (inToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
