using System.Windows.Forms;

using Serilog;

namespace Daedalus.Tools.Iris;

/// <summary>
/// Iris 主面板（iris.md §4）：方式下拉 + 动态参数区（AES / RSA）+ 输入/输出左右分栏 + 状态栏。
/// 界面保持薄，操作编排在 <see cref="IrisOperations"/> / <see cref="IrisAesOperations"/> / <see cref="IrisRsaOperations"/>。
/// </summary>
internal sealed class IrisPanel : UserControl
{
    private readonly ILogger _logger;
    private readonly IrisSettingsStore _settingsStore;

    private readonly ComboBox _methodCombo;
    private readonly Button _executeButton;
    private readonly Button _clearButton;
    private readonly Button _copyButton;

    // AES 参数区
    private readonly FlowLayoutPanel _aesPanel;
    private readonly ComboBox _aesModeCombo;
    private readonly ComboBox _aesKeyBitsCombo;
    private readonly ComboBox _aesKeySourceCombo;
    private readonly Label _aesKeyFormatLabel;
    private readonly ComboBox _aesKeyFormatCombo;
    private readonly Label _aesSecretLabel;
    private readonly TextBox _aesSecretBox;
    private readonly Label _aesIvSourceLabel;
    private readonly ComboBox _aesIvSourceCombo;
    private readonly Label _aesIvLabel;
    private readonly ComboBox _aesIvFormatCombo;
    private readonly TextBox _aesIvBox;
    private readonly Label _aesIvHintLabel;
    private readonly ComboBox _aesCipherFormatCombo;

    // RSA 参数区
    private readonly FlowLayoutPanel _rsaPanel;
    private readonly ComboBox _rsaPaddingCombo;
    private readonly Label _rsaKeyBitsLabel;
    private readonly ComboBox _rsaKeyBitsCombo;
    private readonly ComboBox _rsaCipherFormatCombo;
    private readonly Label _rsaKeyLabel;
    private readonly TextBox _rsaKeyBox;

    private readonly TextBox _inputBox;
    private readonly TextBox _outputBox;
    private readonly ToolStripStatusLabel _statusLabel;

    // 初始化/加载设置期间抑制选择变化事件，避免把未加载完的状态写回 settings.json
    private bool _suppressEvents = true;
    private IrisSettings _settings = IrisSettings.Default;

    /// <summary>
    /// 构造注入（iris.md §4.1）：ILogger 为宿主按插件 id 打好 SourceContext 的实例，
    /// 设置 Store 由容器注入。
    /// </summary>
    public IrisPanel(ILogger logger, IrisSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settingsStore);
        _logger = logger;
        _settingsStore = settingsStore;

        _methodCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(IrisMethod.DisplayName), Width = 150 };
        _executeButton = new Button { Text = "编码", AutoSize = true };
        _clearButton = new Button { Text = "清空", AutoSize = true };
        _copyButton = new Button { Text = "复制输出", AutoSize = true };
        var toolPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        toolPanel.Controls.Add(MakeLabel("方式:"));
        toolPanel.Controls.Add(_methodCombo);
        toolPanel.Controls.Add(_executeButton);
        toolPanel.Controls.Add(_clearButton);
        toolPanel.Controls.Add(_copyButton);

        // AES 参数区
        _aesModeCombo = MakeCombo(new OptionItem<IrisAesCipherMode>(IrisAesCipherMode.Ecb, "ECB"),
            new OptionItem<IrisAesCipherMode>(IrisAesCipherMode.Cbc, "CBC"),
            new OptionItem<IrisAesCipherMode>(IrisAesCipherMode.Gcm, "GCM"));
        _aesKeyBitsCombo = MakeCombo(new OptionItem<int>(128, "128"), new OptionItem<int>(192, "192"), new OptionItem<int>(256, "256"));
        _aesKeySourceCombo = MakeCombo(new OptionItem<IrisAesKeySource>(IrisAesKeySource.Password, "口令（PBKDF2 派生）"),
            new OptionItem<IrisAesKeySource>(IrisAesKeySource.RawKey, "直接输入密钥"));
        _aesKeyFormatLabel = MakeLabel("密钥格式:");
        _aesKeyFormatCombo = MakeCombo(new OptionItem<IrisBytesEncoding>(IrisBytesEncoding.Base64, "Base64"),
            new OptionItem<IrisBytesEncoding>(IrisBytesEncoding.Hex, "HEX"));
        _aesSecretLabel = MakeLabel("口令:");
        _aesSecretBox = new TextBox { Width = 180 };
        _aesIvSourceLabel = MakeLabel("IV:");
        _aesIvSourceCombo = MakeCombo(new OptionItem<IrisAesIvSource>(IrisAesIvSource.Auto, "自动生成并拼接"),
            new OptionItem<IrisAesIvSource>(IrisAesIvSource.Manual, "手动输入"));
        _aesIvLabel = MakeLabel("IV 值:");
        _aesIvFormatCombo = MakeCombo(new OptionItem<IrisBytesEncoding>(IrisBytesEncoding.Base64, "Base64"),
            new OptionItem<IrisBytesEncoding>(IrisBytesEncoding.Hex, "HEX"));
        _aesIvBox = new TextBox { Width = 220 };
        _aesIvHintLabel = MakeLabel(string.Empty);
        _aesCipherFormatCombo = MakeCombo(new OptionItem<IrisBytesEncoding>(IrisBytesEncoding.Base64, "Base64"),
            new OptionItem<IrisBytesEncoding>(IrisBytesEncoding.Hex, "HEX"));
        _aesPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 0, 4, 0) };
        _aesPanel.Controls.Add(MakeLabel("模式:"));
        _aesPanel.Controls.Add(_aesModeCombo);
        _aesPanel.Controls.Add(MakeLabel("密钥长度:"));
        _aesPanel.Controls.Add(_aesKeyBitsCombo);
        _aesPanel.Controls.Add(MakeLabel("密钥来源:"));
        _aesPanel.Controls.Add(_aesKeySourceCombo);
        _aesPanel.Controls.Add(_aesKeyFormatLabel);
        _aesPanel.Controls.Add(_aesKeyFormatCombo);
        _aesPanel.Controls.Add(_aesSecretLabel);
        _aesPanel.Controls.Add(_aesSecretBox);
        _aesPanel.Controls.Add(_aesIvSourceLabel);
        _aesPanel.Controls.Add(_aesIvSourceCombo);
        _aesPanel.Controls.Add(_aesIvLabel);
        _aesPanel.Controls.Add(_aesIvFormatCombo);
        _aesPanel.Controls.Add(_aesIvBox);
        _aesPanel.Controls.Add(_aesIvHintLabel);
        _aesPanel.Controls.Add(MakeLabel("密文格式:"));
        _aesPanel.Controls.Add(_aesCipherFormatCombo);

        // RSA 参数区
        _rsaPaddingCombo = MakeCombo(new OptionItem<IrisRsaPadding>(IrisRsaPadding.OaepSha256, "OAEP-SHA256"),
            new OptionItem<IrisRsaPadding>(IrisRsaPadding.Pkcs1, "PKCS#1 v1.5"));
        _rsaKeyBitsLabel = MakeLabel("密钥长度:");
        _rsaKeyBitsCombo = MakeCombo(new OptionItem<int>(2048, "2048"), new OptionItem<int>(3072, "3072"), new OptionItem<int>(4096, "4096"));
        _rsaCipherFormatCombo = MakeCombo(new OptionItem<IrisBytesEncoding>(IrisBytesEncoding.Base64, "Base64"),
            new OptionItem<IrisBytesEncoding>(IrisBytesEncoding.Hex, "HEX"));
        _rsaKeyLabel = MakeLabel("公钥 (PEM):");
        _rsaKeyBox = new TextBox { Multiline = true, Width = 440, Height = 90, ScrollBars = ScrollBars.Vertical, WordWrap = false };
        _rsaPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 0, 4, 0) };
        _rsaPanel.Controls.Add(MakeLabel("填充:"));
        _rsaPanel.Controls.Add(_rsaPaddingCombo);
        _rsaPanel.Controls.Add(_rsaKeyBitsLabel);
        _rsaPanel.Controls.Add(_rsaKeyBitsCombo);
        _rsaPanel.Controls.Add(MakeLabel("密文格式:"));
        _rsaPanel.Controls.Add(_rsaCipherFormatCombo);
        _rsaPanel.Controls.Add(_rsaKeyLabel);
        _rsaPanel.Controls.Add(_rsaKeyBox);

        _inputBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        _outputBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        splitContainer.Panel1.Controls.Add(_inputBox);
        splitContainer.Panel2.Controls.Add(_outputBox);

        _statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_statusLabel);

        // 后添加的先停靠：状态栏贴底、参数区与工具栏贴顶（后者更靠上），分栏容器填满剩余
        Controls.Add(splitContainer);
        Controls.Add(_rsaPanel);
        Controls.Add(_aesPanel);
        Controls.Add(toolPanel);
        Controls.Add(statusStrip);

        _methodCombo.SelectedIndexChanged += MethodCombo_SelectedIndexChanged;
        foreach (ComboBox combo in new[]
                 {
                     _aesModeCombo, _aesKeyBitsCombo, _aesKeySourceCombo, _aesKeyFormatCombo,
                     _aesIvSourceCombo, _aesIvFormatCombo, _aesCipherFormatCombo,
                     _rsaPaddingCombo, _rsaKeyBitsCombo, _rsaCipherFormatCombo,
                 })
        {
            combo.SelectedIndexChanged += ParameterCombo_SelectedIndexChanged;
        }

        _executeButton.Click += (_, _) => ExecuteCurrent();
        _clearButton.Click += (_, _) => ClearAll();
        _copyButton.Click += (_, _) => CopyOutput();
        Load += IrisPanel_Load;

        foreach (IrisMethod method in IrisOperations.Methods)
        {
            _methodCombo.Items.Add(method);
        }

        // 抑制期内选中默认项，实际恢复由 Load 中的 ApplySettings 完成
        _methodCombo.SelectedIndex = 0;
        UpdateParameterVisibility();
    }

    private IrisMethod? CurrentMethod => _methodCombo.SelectedItem as IrisMethod;

    private static Label MakeLabel(string text)
    {
        return new Label { Text = text, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 0, 0) };
    }

    private static ComboBox MakeCombo<T>(params OptionItem<T>[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(items);
        combo.SelectedIndex = 0;
        return combo;
    }

    /// <summary>下拉项：显示中文文案、携带枚举/数值。</summary>
    private sealed record OptionItem<T>(T Value, string Text)
    {
        public override string ToString()
        {
            return Text;
        }
    }

    private async void IrisPanel_Load(object? sender, EventArgs e)
    {
        // WinForms 事件处理允许 async void（规范 §5），内部必须 try-catch 兜底
        try
        {
            IrisSettingsLoadResult result = await _settingsStore.LoadAsync();
            _settings = result.Settings;
            ApplySettings(result.Settings);

            if (result.RecoveredFromCorruption)
            {
                _logger.Warning("设置文件损坏，已备份到 {BackupPath} 并以默认值启动", result.BackupFilePath);
                _statusLabel.Text = "设置文件损坏，已备份原文件并以默认设置启动";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "加载 Iris 设置失败");
            _statusLabel.Text = $"设置加载失败：{ex.Message}";
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void ApplySettings(IrisSettings settings)
    {
        _methodCombo.SelectedItem = IrisOperations.ResolveInitialMethod(settings.LastMethod);

        IrisAesOptions aes = (settings.Aes ?? IrisAesSettings.FromOptions(IrisAesOptions.Default)).ToOptions();
        SelectOption(_aesModeCombo, aes.Mode);
        SelectOption(_aesKeyBitsCombo, aes.KeyBits);
        SelectOption(_aesKeySourceCombo, aes.KeySource);
        SelectOption(_aesKeyFormatCombo, aes.KeyFormat);
        SelectOption(_aesIvSourceCombo, aes.IvSource);
        SelectOption(_aesIvFormatCombo, aes.IvFormat);
        SelectOption(_aesCipherFormatCombo, aes.CipherFormat);

        IrisRsaOptions rsa = (settings.Rsa ?? IrisRsaSettings.FromOptions(IrisRsaOptions.Default)).ToOptions();
        SelectOption(_rsaPaddingCombo, rsa.Padding);
        SelectOption(_rsaKeyBitsCombo, rsa.KeyBits);
        SelectOption(_rsaCipherFormatCombo, rsa.CipherFormat);

        UpdateExecuteButtonText();
        UpdateParameterVisibility();
    }

    private static void SelectOption<T>(ComboBox combo, T value)
    {
        foreach (object item in combo.Items)
        {
            if (item is OptionItem<T> option && EqualityComparer<T>.Default.Equals(option.Value, value))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static T SelectedValue<T>(ComboBox combo, T fallback)
    {
        return combo.SelectedItem is OptionItem<T> option ? option.Value : fallback;
    }

    private IrisAesOptions CurrentAesOptions()
    {
        IrisAesOptions fallback = IrisAesOptions.Default;
        return new IrisAesOptions(
            SelectedValue(_aesModeCombo, fallback.Mode),
            SelectedValue(_aesKeyBitsCombo, fallback.KeyBits),
            SelectedValue(_aesKeySourceCombo, fallback.KeySource),
            SelectedValue(_aesKeyFormatCombo, fallback.KeyFormat),
            SelectedValue(_aesIvSourceCombo, fallback.IvSource),
            SelectedValue(_aesIvFormatCombo, fallback.IvFormat),
            SelectedValue(_aesCipherFormatCombo, fallback.CipherFormat));
    }

    private IrisRsaOptions CurrentRsaOptions()
    {
        IrisRsaOptions fallback = IrisRsaOptions.Default;
        return new IrisRsaOptions(
            SelectedValue(_rsaPaddingCombo, fallback.Padding),
            SelectedValue(_rsaKeyBitsCombo, fallback.KeyBits),
            SelectedValue(_rsaCipherFormatCombo, fallback.CipherFormat));
    }

    private void MethodCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdateExecuteButtonText();
        UpdateParameterVisibility();

        if (_suppressEvents)
        {
            return;
        }

        SaveSettings();
    }

    private void ParameterCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdateParameterVisibility();

        if (_suppressEvents)
        {
            return;
        }

        SaveSettings();
    }

    private void UpdateExecuteButtonText()
    {
        _executeButton.Text = CurrentMethod?.Category switch
        {
            IrisMethodCategory.Encode => "编码",
            IrisMethodCategory.Decode => "解码",
            IrisMethodCategory.Encrypt => "加密",
            IrisMethodCategory.Decrypt => "解密",
            IrisMethodCategory.Generate => "生成",
            _ => "执行",
        };
    }

    /// <summary>按当前方式与参数显隐参数控件组（iris.md §4）：选中加密类方式时显示对应参数区。</summary>
    private void UpdateParameterVisibility()
    {
        string? id = CurrentMethod?.Id;
        bool isAes = id is IrisOperations.AesEncryptId or IrisOperations.AesDecryptId;
        _aesPanel.Visible = isAes;
        _rsaPanel.Visible = id is IrisOperations.RsaKeygenId or IrisOperations.RsaEncryptId or IrisOperations.RsaDecryptId;

        if (isAes)
        {
            IrisAesOptions options = CurrentAesOptions();
            bool rawKey = options.KeySource == IrisAesKeySource.RawKey;
            _aesKeyFormatLabel.Visible = rawKey;
            _aesKeyFormatCombo.Visible = rawKey;
            _aesSecretLabel.Text = rawKey ? "密钥:" : "口令:";

            // ECB 无 IV；手动 IV 才显示 IV 值输入，并给出长度提示文案
            bool hasIv = options.Mode != IrisAesCipherMode.Ecb;
            bool manualIv = hasIv && options.IvSource == IrisAesIvSource.Manual;
            _aesIvSourceLabel.Visible = hasIv;
            _aesIvSourceCombo.Visible = hasIv;
            _aesIvLabel.Visible = manualIv;
            _aesIvFormatCombo.Visible = manualIv;
            _aesIvBox.Visible = manualIv;
            _aesIvHintLabel.Visible = manualIv;
            if (manualIv)
            {
                string name = options.Mode == IrisAesCipherMode.Gcm ? "nonce" : "IV";
                _aesIvLabel.Text = $"{name} 值:";
                _aesIvHintLabel.Text = $"（{name} 需 {IrisAesOperations.IvLengthOf(options.Mode)} 字节，须与加密时一致）";
            }
        }

        if (_rsaPanel.Visible)
        {
            bool isKeygen = id == IrisOperations.RsaKeygenId;
            _rsaKeyBitsLabel.Visible = isKeygen;
            _rsaKeyBitsCombo.Visible = isKeygen;
            _rsaKeyLabel.Visible = !isKeygen;
            _rsaKeyBox.Visible = !isKeygen;
            _rsaCipherFormatCombo.Visible = !isKeygen;
            _rsaKeyLabel.Text = id == IrisOperations.RsaDecryptId ? "私钥 (PEM):" : "公钥 (PEM):";
        }
    }

    private void SaveSettings()
    {
        _settings = _settings with
        {
            LastMethod = CurrentMethod?.Id,
            Aes = IrisAesSettings.FromOptions(CurrentAesOptions()),
            Rsa = IrisRsaSettings.FromOptions(CurrentRsaOptions()),
        };
        _ = SaveSettingsSafelyAsync(_settings);
    }

    private async Task SaveSettingsSafelyAsync(IrisSettings settings)
    {
        try
        {
            await _settingsStore.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存 Iris 设置失败");
            _statusLabel.Text = $"设置保存失败：{ex.Message}";
        }
    }

    private void ExecuteCurrent()
    {
        if (CurrentMethod is not { } method)
        {
            _statusLabel.Text = "未选择方式，无法执行操作";
            return;
        }

        // 口令/密钥与 IV 值只从界面读取参与运算，绝不写入设置
        IrisOperationResult result = method.Id switch
        {
            IrisOperations.Base64EncodeId or IrisOperations.UrlEncodeId => IrisOperations.Encode(method, _inputBox.Text),
            IrisOperations.Base64DecodeId or IrisOperations.UrlDecodeId or IrisOperations.XmlDecodeId or IrisOperations.JwtDecodeId
                => IrisOperations.Decode(method, _inputBox.Text),
            IrisOperations.AesEncryptId => IrisAesOperations.Encrypt(CurrentAesOptions(), _inputBox.Text, _aesSecretBox.Text, _aesSecretBox.Text, _aesIvBox.Text),
            IrisOperations.AesDecryptId => IrisAesOperations.Decrypt(CurrentAesOptions(), _inputBox.Text, _aesSecretBox.Text, _aesSecretBox.Text, _aesIvBox.Text),
            IrisOperations.RsaKeygenId => IrisRsaOperations.GenerateKeyPair(CurrentRsaOptions()),
            IrisOperations.RsaEncryptId => IrisRsaOperations.Encrypt(CurrentRsaOptions(), _inputBox.Text, _rsaKeyBox.Text),
            IrisOperations.RsaDecryptId => IrisRsaOperations.Decrypt(CurrentRsaOptions(), _inputBox.Text, _rsaKeyBox.Text),
            // 未知 id 属编程错误（方式清单固定），抛出让 App 兜底
            _ => throw new InvalidOperationException($"未知方式：{method.Id}"),
        };
        _statusLabel.Text = result.StatusText;
        if (result.Output is not null)
        {
            _outputBox.Text = result.Output;
        }
    }

    private void ClearAll()
    {
        _inputBox.Clear();
        _outputBox.Clear();
        _statusLabel.Text = string.Empty;
    }

    private void CopyOutput()
    {
        if (string.IsNullOrEmpty(_outputBox.Text))
        {
            _statusLabel.Text = "输出区为空，无内容可复制";
            return;
        }

        Clipboard.SetText(_outputBox.Text);
        _statusLabel.Text = "输出已复制到剪贴板";
    }
}
