using System.Windows.Forms;

using Daedalus.Tools.Hermes.Settings;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// Hermes 设置面板（FR-HERMES-060/061）：跟随重定向 / CookieContainer / 忽略证书校验 /
/// 脚本内存与超时上限 / 历史响应体上限。修改即保存（每个变更立即持久化）。
/// 另提供"立即归档"按钮（FR-HERMES-053 手动入口），归档执行由调用方接线。
/// </summary>
internal sealed class HermesSettingsForm : Form
{
    private readonly Func<HermesSettings, Task> _saveAsync;
    private readonly CheckBox _followRedirectsBox;
    private readonly CheckBox _useCookiesBox;
    private readonly CheckBox _ignoreCertificateBox;
    private readonly NumericUpDown _scriptMemoryInput;
    private readonly NumericUpDown _scriptTimeoutInput;
    private readonly NumericUpDown _bodyLimitInput;
    private readonly Button _archiveButton;

    private HermesSettings _settings;
    private bool _suppressEvents = true;

    public HermesSettingsForm(HermesSettings settings, Func<HermesSettings, Task> saveAsync)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(saveAsync);
        _settings = settings;
        _saveAsync = saveAsync;

        Text = "Hermes 设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(380, 240);

        _followRedirectsBox = new CheckBox { Text = "跟随重定向（默认开）", AutoSize = true };
        _useCookiesBox = new CheckBox { Text = "启用 CookieContainer（默认开）", AutoSize = true };
        _ignoreCertificateBox = new CheckBox { Text = "忽略服务器证书校验（默认关）", AutoSize = true };
        _scriptMemoryInput = new NumericUpDown { Minimum = 1, Maximum = 1024, Width = 90 };
        _scriptTimeoutInput = new NumericUpDown { Minimum = 100, Maximum = 60000, Increment = 500, Width = 90 };
        _bodyLimitInput = new NumericUpDown { Minimum = 1, Maximum = 1024, Width = 90 };
        _archiveButton = new Button { Text = "立即归档历史（30 天前的日文件按月打包）", AutoSize = true };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, Padding = new Padding(10), AutoSize = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        layout.Controls.Add(_followRedirectsBox, 0, 0);
        layout.SetColumnSpan(_followRedirectsBox, 2);
        layout.Controls.Add(_useCookiesBox, 0, 1);
        layout.SetColumnSpan(_useCookiesBox, 2);
        layout.Controls.Add(_ignoreCertificateBox, 0, 2);
        layout.SetColumnSpan(_ignoreCertificateBox, 2);
        layout.Controls.Add(new Label { Text = "脚本内存上限（MB）", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(_scriptMemoryInput, 1, 3);
        layout.Controls.Add(new Label { Text = "脚本超时（毫秒）", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        layout.Controls.Add(_scriptTimeoutInput, 1, 4);
        layout.Controls.Add(new Label { Text = "历史响应体上限（MB）", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 5);
        layout.Controls.Add(_bodyLimitInput, 1, 5);
        layout.Controls.Add(_archiveButton, 0, 6);
        layout.SetColumnSpan(_archiveButton, 2);
        Controls.Add(layout);

        _followRedirectsBox.Checked = settings.FollowRedirects;
        _useCookiesBox.Checked = settings.UseCookies;
        _ignoreCertificateBox.Checked = settings.IgnoreServerCertificate;
        _scriptMemoryInput.Value = ToMB(settings.ScriptMemoryLimitBytes);
        _scriptTimeoutInput.Value = settings.ScriptTimeoutMs;
        _bodyLimitInput.Value = ToMB(settings.ResponseBodyLimitBytes);

        _followRedirectsBox.CheckedChanged += async (_, _) => await SaveAsync();
        _useCookiesBox.CheckedChanged += async (_, _) => await SaveAsync();
        _ignoreCertificateBox.CheckedChanged += async (_, _) => await SaveAsync();
        _scriptMemoryInput.ValueChanged += async (_, _) => await SaveAsync();
        _scriptTimeoutInput.ValueChanged += async (_, _) => await SaveAsync();
        _bodyLimitInput.ValueChanged += async (_, _) => await SaveAsync();
        _archiveButton.Click += (_, _) =>
        {
            // 归档执行由主面板接线（设置面板不持有 HistoryArchive）
            ArchiveRequested?.Invoke(this, EventArgs.Empty);
        };

        _suppressEvents = false;

        // 高 DPI 适配（详见 DpiScale）
        DpiScale.Apply(this);
    }

    /// <summary>设置变化（已成功组织出新值；持久化异步进行）。忽略证书校验开关需要调用方同步到 HttpClientFactory。</summary>
    public event EventHandler<HermesSettings>? SettingsChanged;

    /// <summary>点击"立即归档历史"（FR-HERMES-053 手动入口）；执行与结果反馈由订阅方负责。</summary>
    public event EventHandler? ArchiveRequested;

    private static decimal ToMB(long bytes) => Math.Max(1, bytes / 1024 / 1024);

    private async Task SaveAsync()
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings = _settings with
        {
            FollowRedirects = _followRedirectsBox.Checked,
            UseCookies = _useCookiesBox.Checked,
            IgnoreServerCertificate = _ignoreCertificateBox.Checked,
            ScriptMemoryLimitBytes = (long)_scriptMemoryInput.Value * 1024 * 1024,
            ScriptTimeoutMs = (int)_scriptTimeoutInput.Value,
            ResponseBodyLimitBytes = (long)_bodyLimitInput.Value * 1024 * 1024,
        };
        SettingsChanged?.Invoke(this, _settings);

        try
        {
            await _saveAsync(_settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"设置保存失败：{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
