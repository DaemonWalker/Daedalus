using Daedalus.Tools.Hermes.History;
using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.Settings;

using Serilog;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// Hermes 设置页（FR-HERMES-060/061）：跟随重定向 / CookieContainer / 忽略证书校验 /
/// 脚本内存与超时上限 / 历史响应体上限。修改即保存（每个变更立即持久化）。
/// 由统一设置窗口以标签页承载（FR-SHELL-006），构造注入跨标签共享的 singleton 服务。
/// 另提供"立即归档"按钮（FR-HERMES-053 手动入口）。
/// </summary>
internal sealed class HermesSettingsPanel : UserControl
{
    private readonly HermesSettingsStore _settingsStore;
    private readonly HttpClientFactory _clientFactory;
    private readonly HistoryArchive _historyArchive;
    private readonly ILogger _logger;
    private readonly CheckBox _followRedirectsBox;
    private readonly CheckBox _useCookiesBox;
    private readonly CheckBox _ignoreCertificateBox;
    private readonly NumericUpDown _scriptMemoryInput;
    private readonly NumericUpDown _scriptTimeoutInput;
    private readonly NumericUpDown _bodyLimitInput;

    private HermesSettings _settings = HermesSettings.Default;

    // 加载完成前抑制变更事件，避免把默认控件状态写回 settings.json
    private bool _suppressEvents = true;

    public HermesSettingsPanel(
        HermesSettingsStore settingsStore,
        HttpClientFactory clientFactory,
        HistoryArchive historyArchive,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(historyArchive);
        ArgumentNullException.ThrowIfNull(logger);
        _settingsStore = settingsStore;
        _clientFactory = clientFactory;
        _historyArchive = historyArchive;
        _logger = logger;

        _followRedirectsBox = new CheckBox { Text = "跟随重定向（默认开）", AutoSize = true };
        _useCookiesBox = new CheckBox { Text = "启用 CookieContainer（默认开）", AutoSize = true };
        _ignoreCertificateBox = new CheckBox { Text = "忽略服务器证书校验（默认关）", AutoSize = true };
        _scriptMemoryInput = new NumericUpDown { Minimum = 1, Maximum = 1024, Width = 90 };
        _scriptTimeoutInput = new NumericUpDown { Minimum = 100, Maximum = 60000, Increment = 500, Width = 90 };
        _bodyLimitInput = new NumericUpDown { Minimum = 1, Maximum = 1024, Width = 90 };
        var archiveButton = new Button { Text = "立即归档历史（30 天前的日文件按月打包）", AutoSize = true };

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
        layout.Controls.Add(archiveButton, 0, 6);
        layout.SetColumnSpan(archiveButton, 2);
        Controls.Add(layout);

        _followRedirectsBox.CheckedChanged += async (_, _) => await SaveAsync();
        _useCookiesBox.CheckedChanged += async (_, _) => await SaveAsync();
        _ignoreCertificateBox.CheckedChanged += async (_, _) => await SaveAsync();
        _scriptMemoryInput.ValueChanged += async (_, _) => await SaveAsync();
        _scriptTimeoutInput.ValueChanged += async (_, _) => await SaveAsync();
        _bodyLimitInput.ValueChanged += async (_, _) => await SaveAsync();
        archiveButton.Click += async (_, _) => await RunManualArchiveAsync();
        Load += HermesSettingsPanel_Load;
    }

    private async void HermesSettingsPanel_Load(object? sender, EventArgs e)
    {
        // WinForms 事件处理允许 async void（规范 §5），内部必须 try-catch 兜底
        try
        {
            HermesSettingsLoadResult result = await _settingsStore.LoadAsync();
            _settings = result.Settings;
            if (result.RecoveredFromCorruption)
            {
                _logger.Warning("设置文件损坏，已备份到 {BackupPath} 并以默认值启动", result.BackupFilePath);
                MessageBox.Show(this, "设置文件损坏，已备份原文件并以默认设置启动。", "Hermes 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            _followRedirectsBox.Checked = _settings.FollowRedirects;
            _useCookiesBox.Checked = _settings.UseCookies;
            _ignoreCertificateBox.Checked = _settings.IgnoreServerCertificate;
            _scriptMemoryInput.Value = ToMB(_settings.ScriptMemoryLimitBytes);
            _scriptTimeoutInput.Value = _settings.ScriptTimeoutMs;
            _bodyLimitInput.Value = ToMB(_settings.ResponseBodyLimitBytes);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "加载 Hermes 设置失败");
            MessageBox.Show(this, $"设置加载失败：{ex.Message}", "Hermes 设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private static decimal ToMB(long bytes) => Math.Max(1, bytes / 1024 / 1024);

    private async Task SaveAsync()
    {
        if (_suppressEvents)
        {
            return;
        }

        HermesSettings previous = _settings;
        _settings = _settings with
        {
            FollowRedirects = _followRedirectsBox.Checked,
            UseCookies = _useCookiesBox.Checked,
            IgnoreServerCertificate = _ignoreCertificateBox.Checked,
            ScriptMemoryLimitBytes = (long)_scriptMemoryInput.Value * 1024 * 1024,
            ScriptTimeoutMs = (int)_scriptTimeoutInput.Value,
            ResponseBodyLimitBytes = (long)_bodyLimitInput.Value * 1024 * 1024,
        };

        if (_settings.IgnoreServerCertificate != previous.IgnoreServerCertificate)
        {
            // 证书校验开关变化 → 销毁重建双 client（hermes.md §5.2，FR-HERMES-008）
            _clientFactory.SetIgnoreServerCertificate(_settings.IgnoreServerCertificate);
        }

        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存 Hermes 设置失败");
            MessageBox.Show(this, $"设置保存失败：{ex.Message}", "Hermes 设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>"立即归档"（FR-HERMES-053 手动入口）：执行归档并反馈结果。</summary>
    private async Task RunManualArchiveAsync()
    {
        try
        {
            HistoryArchiveResult result = await _historyArchive.ArchiveOldFilesAsync();
            string message;
            if (result.ArchivedMonths.Count == 0 && result.SkippedMonths.Count == 0 && result.FailedMonths.Count == 0)
            {
                message = "没有需要归档的历史文件。";
            }
            else
            {
                var lines = new List<string>();
                if (result.ArchivedMonths.Count > 0)
                {
                    lines.Add($"已归档（{result.Compressor}）：{string.Join("、", result.ArchivedMonths)}");
                }
                if (result.SkippedMonths.Count > 0)
                {
                    lines.Add($"已跳过（归档包已存在，原文件保留）：{string.Join("、", result.SkippedMonths)}");
                }
                if (result.FailedMonths.Count > 0)
                {
                    lines.Add($"归档失败（原文件保留，详见日志）：{string.Join("、", result.FailedMonths)}");
                }

                message = string.Join('\n', lines);
            }

            MessageBox.Show(this, message, "历史归档", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "手动归档历史失败");
            MessageBox.Show(this, $"归档失败：{ex.Message}", "历史归档", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
