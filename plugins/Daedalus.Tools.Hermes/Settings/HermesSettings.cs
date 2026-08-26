namespace Daedalus.Tools.Hermes.Settings;

/// <summary>Hermes 的工具设置（hermes.md §11.4，FR-HERMES-060/061）。</summary>
/// <param name="Version">设置文件格式版本，当前为 1。</param>
/// <param name="FollowRedirects">全局默认是否跟随重定向（默认开）。</param>
/// <param name="UseCookies">全局默认是否使用共享 CookieContainer（默认开）。</param>
/// <param name="IgnoreServerCertificate">是否忽略服务器证书校验（默认关，FR-HERMES-008）。</param>
/// <param name="ScriptMemoryLimitBytes">后事件脚本内存上限（NFR-002，默认 4 MB）。</param>
/// <param name="ScriptTimeoutMs">后事件脚本超时毫秒数（NFR-002，默认 2000）。</param>
/// <param name="ResponseBodyLimitBytes">历史记录单条响应体上限（FR-HERMES-050，默认 10 MB）。</param>
public sealed record HermesSettings(
    int Version,
    bool FollowRedirects,
    bool UseCookies,
    bool IgnoreServerCertificate,
    long ScriptMemoryLimitBytes,
    int ScriptTimeoutMs,
    long ResponseBodyLimitBytes)
{
    /// <summary>当前设置文件格式版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>默认脚本内存上限：4 MB。</summary>
    public const long DefaultScriptMemoryLimitBytes = 4L * 1024 * 1024;

    /// <summary>默认脚本超时：2000 ms。</summary>
    public const int DefaultScriptTimeoutMs = 2000;

    /// <summary>默认历史响应体上限：10 MB。</summary>
    public const long DefaultResponseBodyLimitBytes = 10L * 1024 * 1024;

    /// <summary>首次启动或设置文件损坏恢复时使用的默认设置。</summary>
    public static HermesSettings Default { get; } = new(
        CurrentVersion,
        FollowRedirects: true,
        UseCookies: true,
        IgnoreServerCertificate: false,
        ScriptMemoryLimitBytes: DefaultScriptMemoryLimitBytes,
        ScriptTimeoutMs: DefaultScriptTimeoutMs,
        ResponseBodyLimitBytes: DefaultResponseBodyLimitBytes);

    // 上限值非正数只会出现在手工改坏文件的场景，按 DR-003 备份恢复更直白
    internal bool IsValid => ScriptMemoryLimitBytes > 0 && ScriptTimeoutMs > 0 && ResponseBodyLimitBytes > 0;
}
