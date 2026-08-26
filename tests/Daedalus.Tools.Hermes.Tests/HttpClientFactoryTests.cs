using Daedalus.Tools.Hermes.Http;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>HttpClientFactory 缓存与重建语义（hermes.md §5.2）。</summary>
public sealed class HttpClientFactoryTests
{
    [Fact]
    public void GetClient_相同Cookie设置_返回缓存的同一实例()
    {
        using var factory = new HttpClientFactory();

        HttpClient withCookies1 = factory.GetClient(useCookies: true);
        HttpClient withCookies2 = factory.GetClient(useCookies: true);
        HttpClient withoutCookies1 = factory.GetClient(useCookies: false);
        HttpClient withoutCookies2 = factory.GetClient(useCookies: false);

        Assert.Same(withCookies1, withCookies2);
        Assert.Same(withoutCookies1, withoutCookies2);
        Assert.NotSame(withCookies1, withoutCookies1);
    }

    [Fact]
    public void SetIgnoreServerCertificate_开关变化_销毁并重建两个client()
    {
        using var factory = new HttpClientFactory();
        HttpClient oldWithCookies = factory.GetClient(useCookies: true);
        HttpClient oldWithoutCookies = factory.GetClient(useCookies: false);

        factory.SetIgnoreServerCertificate(true);

        Assert.True(factory.IgnoreServerCertificate);
        Assert.NotSame(oldWithCookies, factory.GetClient(useCookies: true));
        Assert.NotSame(oldWithoutCookies, factory.GetClient(useCookies: false));
    }

    [Fact]
    public void SetIgnoreServerCertificate_开关未变化_保留缓存实例()
    {
        using var factory = new HttpClientFactory();
        HttpClient client = factory.GetClient(useCookies: true);

        factory.SetIgnoreServerCertificate(false);

        Assert.False(factory.IgnoreServerCertificate);
        Assert.Same(client, factory.GetClient(useCookies: true));
    }

    [Fact]
    public void SetIgnoreServerCertificate_重建后_Cookie容器保持同一引用()
    {
        using var factory = new HttpClientFactory();
        System.Net.CookieContainer cookies = factory.Cookies;

        factory.SetIgnoreServerCertificate(true);
        factory.GetClient(useCookies: true);

        Assert.Same(cookies, factory.Cookies);
    }

    [Fact]
    public void GetClient_工厂已释放_抛ObjectDisposedException()
    {
        var factory = new HttpClientFactory();
        factory.Dispose();

        Assert.Throws<ObjectDisposedException>(() => factory.GetClient(useCookies: true));
    }
}
