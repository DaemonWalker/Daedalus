using System.Text;

namespace Daedalus.Tools.Hermes.Variables;

/// <summary>变量替换结果。</summary>
/// <param name="Text">替换后的文本。</param>
/// <param name="UndefinedVariables">本次替换中未定义（或已停用）的变量名清单，按首次出现顺序去重；原样保留在 <paramref name="Text"/> 中。</param>
public sealed record VariableResolutionResult(string Text, IReadOnlyList<string> UndefinedVariables)
{
    /// <summary>是否存在未定义变量（调用方据此在状态栏警告，FR-HERMES-022）。</summary>
    public bool HasUndefinedVariables => UndefinedVariables.Count > 0;
}

/// <summary>
/// <c>{{变量名}}</c> 替换（hermes.md §6）：变量名允许字母、数字、_、-、.；
/// 未定义变量原样保留并列入结果清单；<c>\{{</c> 转义输出字面量 <c>{{</c>；
/// 未启用环境（environment 为 null）时所有变量均视为未定义。
/// </summary>
public sealed class VariableResolver
{
    /// <summary>替换 <paramref name="input"/> 中的变量引用，从 <paramref name="environment"/> 中按名查找（仅取 enabled 的变量）。</summary>
    public VariableResolutionResult Resolve(string input, HermesEnvironment? environment)
    {
        ArgumentNullException.ThrowIfNull(input);

        var builder = new StringBuilder(input.Length);
        var undefined = new List<string>();
        int i = 0;
        while (i < input.Length)
        {
            // 转义：\{{ 输出字面量 {{，不做变量解析
            if (input[i] == '\\' && i + 2 < input.Length && input[i + 1] == '{' && input[i + 2] == '{')
            {
                builder.Append("{{");
                i += 3;
                continue;
            }

            if (input[i] == '{' && i + 1 < input.Length && input[i + 1] == '{')
            {
                int end = input.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    string name = input[(i + 2)..end];
                    if (name.All(IsNameChar))
                    {
                        EnvironmentVariable? variable = environment?.Variables.FirstOrDefault(v => v.Enabled && v.Key == name);
                        if (variable is not null)
                        {
                            builder.Append(variable.Value);
                        }
                        else
                        {
                            // 未定义：原样保留并记录，供状态栏警告（FR-HERMES-022）
                            builder.Append(input, i, end + 2 - i);
                            if (!undefined.Contains(name, StringComparer.Ordinal))
                            {
                                undefined.Add(name);
                            }
                        }

                        i = end + 2;
                        continue;
                    }
                }
            }

            builder.Append(input[i]);
            i++;
        }

        return new VariableResolutionResult(builder.ToString(), undefined);
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.';
}
