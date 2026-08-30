namespace Daedalus.Tools.Iris;

/// <summary>Iris 的用户设置（iris.md §6）：记住上次方式与 AES / RSA 参数选择；绝不持久化密钥/口令/IV 值。</summary>
/// <param name="Version">设置文件格式版本，当前为 1。</param>
/// <param name="LastMethod">上次选择的方式 id；尚未选择过（首次启动）为 null。</param>
/// <param name="Aes">AES 参数选择；未用过 AES 方式为 null。</param>
/// <param name="Rsa">RSA 参数选择；未用过 RSA 方式为 null。</param>
public sealed record IrisSettings(int Version, string? LastMethod, IrisAesSettings? Aes, IrisRsaSettings? Rsa)
{
    /// <summary>当前设置文件格式版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>首次启动或设置文件损坏恢复时使用的默认设置。</summary>
    public static IrisSettings Default { get; } = new(CurrentVersion, null, null, null);
}

/// <summary>AES 参数选择的持久化形式：枚举以名称字符串存储、长度以数值存储，未知值读取时回落默认。</summary>
public sealed record IrisAesSettings(
    string? Mode,
    int? KeyBits,
    string? KeySource,
    string? KeyFormat,
    string? IvSource,
    string? IvFormat,
    string? CipherFormat)
{
    /// <summary>从当前参数选择快照出持久化形式。</summary>
    public static IrisAesSettings FromOptions(IrisAesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new IrisAesSettings(
            options.Mode.ToString(),
            options.KeyBits,
            options.KeySource.ToString(),
            options.KeyFormat.ToString(),
            options.IvSource.ToString(),
            options.IvFormat.ToString(),
            options.CipherFormat.ToString());
    }

    /// <summary>还原为参数选择：字段缺失、未知枚举名、非法长度均容忍并回落 <see cref="IrisAesOptions.Default"/> 对应项。</summary>
    public IrisAesOptions ToOptions()
    {
        IrisAesOptions fallback = IrisAesOptions.Default;
        return new IrisAesOptions(
            ParseEnum(Mode, fallback.Mode),
            KeyBits is 128 or 192 or 256 ? KeyBits.Value : fallback.KeyBits,
            ParseEnum(KeySource, fallback.KeySource),
            ParseEnum(KeyFormat, fallback.KeyFormat),
            ParseEnum(IvSource, fallback.IvSource),
            ParseEnum(IvFormat, fallback.IvFormat),
            ParseEnum(CipherFormat, fallback.CipherFormat));
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        // IsDefined 挡掉 "5" 这类数值串解析出的未定义枚举值
        return Enum.TryParse(value, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed) ? parsed : fallback;
    }
}

/// <summary>RSA 参数选择的持久化形式。</summary>
public sealed record IrisRsaSettings(string? Padding, int? KeyBits, string? CipherFormat)
{
    /// <summary>从当前参数选择快照出持久化形式。</summary>
    public static IrisRsaSettings FromOptions(IrisRsaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new IrisRsaSettings(options.Padding.ToString(), options.KeyBits, options.CipherFormat.ToString());
    }

    /// <summary>还原为参数选择：字段缺失、未知枚举名、非法长度均容忍并回落 <see cref="IrisRsaOptions.Default"/> 对应项。</summary>
    public IrisRsaOptions ToOptions()
    {
        IrisRsaOptions fallback = IrisRsaOptions.Default;
        return new IrisRsaOptions(
            ParseEnum(Padding, fallback.Padding),
            KeyBits is 2048 or 3072 or 4096 ? KeyBits.Value : fallback.KeyBits,
            ParseEnum(CipherFormat, fallback.CipherFormat));
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed) ? parsed : fallback;
    }
}
