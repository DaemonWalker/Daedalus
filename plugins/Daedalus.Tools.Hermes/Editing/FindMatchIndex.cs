namespace Daedalus.Tools.Hermes.Editing;

/// <summary>
/// 查找条的匹配核心（不含 UI）：忽略大小写的普通子串匹配 + 环绕导航下标计算，
/// 与 <c>View/FindBar</c> 解耦以便单测。
/// </summary>
internal static class FindMatchIndex
{
    /// <summary>找出全部命中起点（Ordinal 忽略大小写、非重叠），升序返回；空词或无命中返回空数组。</summary>
    public static int[] FindAll(string text, string query)
    {
        if (string.IsNullOrEmpty(query) || text.Length < query.Length)
        {
            return [];
        }

        var matches = new List<int>();
        int index = 0;
        while ((index = text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            matches.Add(index);
            index += query.Length; // 非重叠推进：命中后跳过整个词长
        }

        return [.. matches];
    }

    /// <summary>下一个命中下标（到末尾回绕到 0）；无命中返回 -1，current 为 -1 时落在首个命中。</summary>
    public static int WrapNext(int current, int count) => count <= 0 ? -1 : (current + 1) % count;

    /// <summary>上一个命中下标（到开头回绕到末尾）；无命中返回 -1，current ≤ 0 时落在最后一个命中。</summary>
    public static int WrapPrevious(int current, int count) => count <= 0 ? -1 : current <= 0 ? count - 1 : (current - 1) % count;

    /// <summary>首个起点 ≥ position 的命中下标；全部命中都在 position 之前时回绕到 0；无命中返回 -1。</summary>
    public static int IndexAtOrAfter(int[] matches, int position)
    {
        if (matches.Length == 0)
        {
            return -1;
        }

        for (int i = 0; i < matches.Length; i++)
        {
            if (matches[i] >= position)
            {
                return i;
            }
        }

        return 0;
    }
}
