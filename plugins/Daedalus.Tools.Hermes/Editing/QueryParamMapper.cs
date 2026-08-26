using Daedalus.Tools.Hermes.Collections;

namespace Daedalus.Tools.Hermes.Editing;

/// <summary>
/// URL query 部分与 Params 编辑表的双向映射（hermes.md §3 的 Params 页）：
/// 集合模型不单独存储 query 参数（§11.1），Params 页是 URL query 段的编辑视图。
/// </summary>
public static class QueryParamMapper
{
    /// <summary>解析 URL 的 query 部分为键值表；无 query 返回空表。键为空（如 "?=1" 或裸 "&"）的段跳过。</summary>
    public static List<KeyValueEntry> Parse(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var entries = new List<KeyValueEntry>();
        int queryStart = url.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0 || queryStart + 1 >= url.Length)
        {
            return entries;
        }

        string query = url[(queryStart + 1)..];
        int fragmentStart = query.IndexOf('#', StringComparison.Ordinal);
        if (fragmentStart >= 0)
        {
            query = query[..fragmentStart];
        }

        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=', StringComparison.Ordinal);
            string key = Decode(eq < 0 ? pair : pair[..eq]);
            if (key.Length == 0)
            {
                continue;
            }

            entries.Add(new KeyValueEntry(key, eq < 0 ? string.Empty : Decode(pair[(eq + 1)..])));
        }

        return entries;
    }

    /// <summary>用键值表重建 URL：保留 query 前的部分与 # 片段，enabled 且键非空的项参与拼接。</summary>
    public static string Apply(string url, IReadOnlyList<KeyValueEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(entries);

        string basePart = url;
        string fragment = string.Empty;
        int fragmentStart = basePart.IndexOf('#', StringComparison.Ordinal);
        if (fragmentStart >= 0)
        {
            fragment = basePart[fragmentStart..];
            basePart = basePart[..fragmentStart];
        }

        int queryStart = basePart.IndexOf('?', StringComparison.Ordinal);
        if (queryStart >= 0)
        {
            basePart = basePart[..queryStart];
        }

        string query = string.Join("&",
            entries.Where(e => e.Enabled && e.Key.Length > 0)
                .Select(e => $"{Encode(e.Key)}={Encode(e.Value)}"));
        return query.Length == 0 ? basePart + fragment : $"{basePart}?{query}{fragment}";
    }

    private static string Encode(string text) => Uri.EscapeDataString(text);

    private static string Decode(string text) => Uri.UnescapeDataString(text);
}
