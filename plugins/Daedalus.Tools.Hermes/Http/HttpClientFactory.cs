using System.Net;

namespace Daedalus.Tools.Hermes.Http;

/// <summary>
/// 按设置构建并缓存 <see cref="HttpClient"/>（hermes.md §5.2）：
/// Cookie 与证书校验行为只能在 handler 构造时确定，因此维护带/不带 Cookie 两个缓存实例，
/// "忽略证书校验"开关变化时销毁重建。
/// </summary>
public sealed class HttpClientFactory : IDisposable
{
    private readonly object _sync = new();

    // 测试注入的 handler 工厂；为 null 时构造真实 HttpClientHandler
    private readonly Func<HttpMessageHandler>? _handlerFactory;

    private HttpClient? _withCookies;
    private HttpClient? _withoutCookies;
    private bool _disposed;

    /// <summary>创建使用真实网络 handler 的工厂。</summary>
    public HttpClientFactory()
    {
    }

    /// <summary>创建使用注入 handler 的工厂（仅测试用，注入 handler 不具备真实 Cookie/证书行为）。</summary>
    internal HttpClientFactory(Func<HttpMessageHandler> handlerFactory)
    {
        _handlerFactory = handlerFactory;
    }

    /// <summary>工具生命周期内共享的 CookieContainer（"浏览器会话"，FR-HERMES-007）。</summary>
    public CookieContainer Cookies { get; } = new();

    /// <summary>当前是否忽略服务器证书校验（FR-HERMES-008）。</summary>
    public bool IgnoreServerCertificate { get; private set; }

    /// <summary>按生效的 Cookie 设置取缓存的 client；不存在时按需创建。</summary>
    /// <param name="useCookies">true 使用共享 <see cref="Cookies"/>；false 完全不收发 Cookie。</param>
    public HttpClient GetClient(bool useCookies)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return useCookies
                ? _withCookies ??= CreateClient(useCookies: true)
                : _withoutCookies ??= CreateClient(useCookies: false);
        }
    }

    /// <summary>切换"忽略证书校验"开关；变化时销毁两个缓存 client，下次取用按新开关重建。</summary>
    public void SetIgnoreServerCertificate(bool ignore)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ignore == IgnoreServerCertificate)
            {
                return;
            }

            IgnoreServerCertificate = ignore;
            _withCookies?.Dispose();
            _withCookies = null;
            _withoutCookies?.Dispose();
            _withoutCookies = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _withCookies?.Dispose();
            _withoutCookies?.Dispose();
        }
    }

    private HttpClient CreateClient(bool useCookies)
    {
        HttpMessageHandler handler = _handlerFactory?.Invoke() ?? CreateDefaultHandler(useCookies);
        return new HttpClient(handler, disposeHandler: true);
    }

    private HttpClientHandler CreateDefaultHandler(bool useCookies)
    {
        var handler = new HttpClientHandler
        {
            // 重定向一律由 HttpEngine 手动跟随（hermes.md §5.3），handler 层永不自动跳转
            AllowAutoRedirect = false,
            UseCookies = useCookies,
            // 不开 AutomaticDecompression：它会向用户指定的 Accept-Encoding 并集 handler 自己的值，
            // 破坏"请求头原样发出"的语义；解压由引擎侧 ResponseBodyDecoder 按响应 Content-Encoding 处理
        };
        if (useCookies)
        {
            handler.CookieContainer = Cookies;
        }

        if (IgnoreServerCertificate)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return handler;
    }
}
