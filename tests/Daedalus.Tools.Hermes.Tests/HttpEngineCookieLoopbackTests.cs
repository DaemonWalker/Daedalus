using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.Settings;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>
/// Cookie 跨跳共享的回环测试（hermes.md §5.3：Set-Cookie 逐跳生效）。
/// HttpListener 在 Windows 非管理员下受 URL ACL 限制，这里用原始 TcpListener 实现迷你 HTTP 服务器。
/// </summary>
public sealed class HttpEngineCookieLoopbackTests
{
    /// <summary>单连接单请求的迷你 HTTP 服务器：/step1 种 Cookie 并跳转 /step2，/step2 回显收到的 Cookie 头。</summary>
    private sealed class MiniHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public MiniHttpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        /// <summary>各连接收到的 Cookie 请求头（无 Cookie 时为 null），按到达顺序记录。</summary>
        public ConcurrentQueue<string?> ReceivedCookies { get; } = new();

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            try
            {
                _acceptLoop.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // 停服时接受循环被取消属预期，无需处理
            }

            _cts.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break; // Stop() 中止监听
                }

                _ = Task.Run(() => HandleAsync(client));
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                string headerText = await ReadHeadersAsync(stream);
                string requestLine = headerText.Split("\r\n")[0];
                string path = requestLine.Split(' ')[1];
                string? cookie = headerText.Split("\r\n")
                    .Where(l => l.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
                    .Select(l => l["Cookie:".Length..].Trim())
                    .FirstOrDefault();
                ReceivedCookies.Enqueue(cookie);

                (int status, string? location, string? setCookie, string body) = path switch
                {
                    "/step1" => (302, "/step2", "sid=abc; Path=/", "redirecting"),
                    _ => (200, null, null, cookie ?? ""),
                };

                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                var sb = new StringBuilder();
                sb.Append("HTTP/1.1 ").Append(status).Append(status == 302 ? " Found" : " OK").Append("\r\n");
                if (location is not null)
                {
                    sb.Append("Location: ").Append(location).Append("\r\n");
                }

                if (setCookie is not null)
                {
                    sb.Append("Set-Cookie: ").Append(setCookie).Append("\r\n");
                }

                sb.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
                sb.Append("Connection: close\r\n\r\n");
                byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
                await stream.WriteAsync(headerBytes);
                await stream.WriteAsync(bodyBytes);
            }
        }

        private static async Task<string> ReadHeadersAsync(NetworkStream stream)
        {
            // 只读请求头（测试请求均无请求体），读到 \r\n\r\n 为止
            var buffer = new MemoryStream();
            byte[] chunk = new byte[1024];
            while (true)
            {
                int read = await stream.ReadAsync(chunk);
                if (read == 0)
                {
                    break;
                }

                buffer.Write(chunk, 0, read);
                if (Encoding.ASCII.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    break;
                }
            }

            return Encoding.ASCII.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
    }

    private static SendRequest Get(string url, RequestOptions? options = null) => new("GET", url, [], null, options);

    [Fact]
    public async Task SendAsync_Cookie启用_SetCookie跨跳生效()
    {
        using var server = new MiniHttpServer();
        using var factory = new HttpClientFactory();
        var engine = new HttpEngine(factory);

        SendResult result = await engine.SendAsync(Get($"http://127.0.0.1:{server.Port}/step1"), HermesSettings.Default);

        Assert.Equal(2, result.Hops.Count);
        Assert.Equal(302, result.Hops[0].Response.Status);
        Assert.Equal(200, result.FinalHop.Response.Status);
        Assert.Contains("sid=abc", result.FinalHop.Response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_Cookie禁用_不携带Cookie()
    {
        using var server = new MiniHttpServer();
        using var factory = new HttpClientFactory();
        var engine = new HttpEngine(factory);

        SendResult result = await engine.SendAsync(
            Get($"http://127.0.0.1:{server.Port}/step1", new RequestOptions(FollowRedirect: null, UseCookies: false)),
            HermesSettings.Default);

        Assert.Equal(2, result.Hops.Count);
        Assert.Equal(200, result.FinalHop.Response.Status);
        Assert.Equal("", result.FinalHop.Response.Body);
    }

    [Fact]
    public async Task SendAsync_Cookie启用_同工厂后续请求共享会话()
    {
        using var server = new MiniHttpServer();
        using var factory = new HttpClientFactory();
        var engine = new HttpEngine(factory);
        string baseUrl = $"http://127.0.0.1:{server.Port}";

        await engine.SendAsync(Get($"{baseUrl}/step1"), HermesSettings.Default);
        // 第二次直接请求 /step2：上一请求种的 Cookie 应自动带上（共享 CookieContainer）
        SendResult second = await engine.SendAsync(Get($"{baseUrl}/step2"), HermesSettings.Default);

        Assert.Contains("sid=abc", second.FinalHop.Response.Body, StringComparison.Ordinal);
    }
}
