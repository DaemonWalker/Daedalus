namespace Daedalus.Tools.Iris;

/// <summary>RSA 填充方式。</summary>
public enum IrisRsaPadding
{
    /// <summary>OAEP-SHA256（推荐）。</summary>
    OaepSha256,

    /// <summary>PKCS#1 v1.5（兼容旧系统）。</summary>
    Pkcs1,
}

/// <summary>RSA 操作的参数选择（iris.md §5.2；不含密钥材料——PEM 由界面运行时传入，绝不持久化）。</summary>
/// <param name="Padding">填充方式。</param>
/// <param name="KeyBits">密钥长度：2048 / 3072 / 4096（仅生成密钥对生效）。</param>
/// <param name="CipherFormat">密文输入输出格式。</param>
public sealed record IrisRsaOptions(IrisRsaPadding Padding, int KeyBits, IrisBytesEncoding CipherFormat)
{
    /// <summary>默认参数：OAEP-SHA256 / 2048 位 / 密文 Base64。</summary>
    public static IrisRsaOptions Default { get; } = new(IrisRsaPadding.OaepSha256, 2048, IrisBytesEncoding.Base64);
}
