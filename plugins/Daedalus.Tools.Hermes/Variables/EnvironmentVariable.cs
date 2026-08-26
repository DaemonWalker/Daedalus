namespace Daedalus.Tools.Hermes.Variables;

/// <summary>环境变量（hermes.md §11.2）。</summary>
/// <param name="Key">变量名，请求中以 <c>{{变量名}}</c> 引用。</param>
/// <param name="Value">变量值。secret 变量在文件中同样明文存储（FR-HERMES-023）。</param>
/// <param name="Secret">true 表示界面上掩码显示（仅控制显示，不影响存储）。</param>
/// <param name="Enabled">false 表示该变量不参与替换。</param>
public sealed record EnvironmentVariable(string Key, string Value, bool Secret = false, bool Enabled = true);
