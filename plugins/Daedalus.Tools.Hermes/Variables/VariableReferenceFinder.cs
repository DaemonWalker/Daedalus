namespace Daedalus.Tools.Hermes.Variables;

/// <summary>文本中的一处 <c>{{变量名}}</c> 引用。</summary>
/// <param name="Name">变量名（不含大括号）。</param>
/// <param name="Start">引用起始字符下标（含 <c>{{</c>）。</param>
/// <param name="Length">引用整段长度（含 <c>{{ }}</c>）。</param>
public sealed record VariableReference(string Name, int Start, int Length);

/// <summary>
/// <c>{{变量名}}</c> 引用的文本命中检测（FR-HERMES-024 悬浮编辑的定位基础）：
/// 语法口径与 <see cref="VariableResolver"/> 一致——变量名允许字母、数字、_、-、.，
/// <c>\{{</c> 转义段不算引用，未闭合或含非法字符的 <c>{{</c> 按字面量跳过。
/// </summary>
public static class VariableReferenceFinder
{
    /// <summary>找出文本中的全部变量引用，按出现顺序。</summary>
    public static IReadOnlyList<VariableReference> FindAll(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var references = new List<VariableReference>();
        int i = 0;
        while (i < text.Length)
        {
            // 转义：\{{ 是字面量，不是引用（与 VariableResolver 口径一致）
            if (text[i] == '\\' && i + 2 < text.Length && text[i + 1] == '{' && text[i + 2] == '{')
            {
                i += 3;
                continue;
            }

            if (text[i] == '{' && i + 1 < text.Length && text[i + 1] == '{')
            {
                int end = text.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    string name = text[(i + 2)..end];
                    if (name.All(IsNameChar))
                    {
                        references.Add(new VariableReference(name, i, end + 2 - i));
                        i = end + 2;
                        continue;
                    }
                }
            }

            i++;
        }

        return references;
    }

    /// <summary>查找覆盖字符下标 <paramref name="charIndex"/> 的引用；不在任何引用范围内时返回 null。</summary>
    public static VariableReference? FindAt(string text, int charIndex)
    {
        ArgumentNullException.ThrowIfNull(text);
        return FindAll(text).FirstOrDefault(r => charIndex >= r.Start && charIndex < r.Start + r.Length);
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.';
}
