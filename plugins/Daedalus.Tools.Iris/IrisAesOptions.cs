namespace Daedalus.Tools.Iris;

/// <summary>AES 工作模式。</summary>
public enum IrisAesCipherMode
{
    /// <summary>ECB（无 IV；仅兼容用途，语义安全性弱）。</summary>
    Ecb,

    /// <summary>CBC（IV 16 字节）。</summary>
    Cbc,

    /// <summary>GCM（nonce 12 字节，认证标签 16 字节附密文尾部；带完整性校验）。</summary>
    Gcm,
}

/// <summary>AES 密钥来源。</summary>
public enum IrisAesKeySource
{
    /// <summary>口令：PBKDF2-SHA256 派生（100,000 次迭代 + 16 字节随机盐）。</summary>
    Password,

    /// <summary>直接输入密钥（HEX / Base64 文本，长度须与所选密钥长度严格匹配）。</summary>
    RawKey,
}

/// <summary>字节序列的文本编码（密钥 / IV / 密文输入输出共用）。</summary>
public enum IrisBytesEncoding
{
    /// <summary>Base64。</summary>
    Base64,

    /// <summary>十六进制（输出大写；解析大小写均可）。</summary>
    Hex,
}

/// <summary>AES IV / nonce 来源（ECB 模式无 IV，忽略此项）。</summary>
public enum IrisAesIvSource
{
    /// <summary>自动：随机生成并拼在密文头部输出（解密时从密文拆分）。</summary>
    Auto,

    /// <summary>手动：用户输入（HEX / Base64），输出仅密文本体。</summary>
    Manual,
}

/// <summary>AES 操作的参数选择（iris.md §5.1；不含密钥/口令/IV 值本身——运行时由界面传入，绝不持久化）。</summary>
/// <param name="Mode">工作模式。</param>
/// <param name="KeyBits">密钥长度：128 / 192 / 256。</param>
/// <param name="KeySource">密钥来源。</param>
/// <param name="KeyFormat">直接密钥的文本格式（仅 <see cref="IrisAesKeySource.RawKey"/> 生效）。</param>
/// <param name="IvSource">IV / nonce 来源（仅 CBC / GCM 生效）。</param>
/// <param name="IvFormat">手动 IV 的文本格式（仅 <see cref="IrisAesIvSource.Manual"/> 生效）。</param>
/// <param name="CipherFormat">密文输入输出格式。</param>
public sealed record IrisAesOptions(
    IrisAesCipherMode Mode,
    int KeyBits,
    IrisAesKeySource KeySource,
    IrisBytesEncoding KeyFormat,
    IrisAesIvSource IvSource,
    IrisBytesEncoding IvFormat,
    IrisBytesEncoding CipherFormat)
{
    /// <summary>默认参数：CBC / 256 位 / 口令派生 / 自动 IV / 密文 Base64。</summary>
    public static IrisAesOptions Default { get; } = new(
        IrisAesCipherMode.Cbc,
        256,
        IrisAesKeySource.Password,
        IrisBytesEncoding.Base64,
        IrisAesIvSource.Auto,
        IrisBytesEncoding.Base64,
        IrisBytesEncoding.Base64);

    /// <summary>密钥字节数。</summary>
    public int KeyBytes => KeyBits / 8;
}
