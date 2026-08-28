using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.Settings;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>
/// Accept-Encoding / 引擎侧解压的回环测试（hermes.md §5.1/§5.2）：真实 HttpClientFactory + 迷你 HTTP 服务器，
/// 验证用户指定的 Accept-Encoding 原样到达服务器（handler 不并集改写）、gzip 响应按 Content-Encoding 解压。
/// </summary>
public sealed class HttpEngineDecompressionLoopbackTests
{
    /// <summary>单连接单请求的迷你 HTTP 服务器：任何路径都返回 gzip 压缩的固定文本。</summary>
    private sealed class GzipHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public GzipHttpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        /// <summary>各连接收到的 Accept-Encoding 请求头（无该头时为 null），按到达顺序记录。</summary>
        public ConcurrentQueue<string?> ReceivedAcceptEncoding { get; } = new();

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
                ReceivedAcceptEncoding.Enqueue(headerText.Split("\r\n")
                    .Where(l => l.StartsWith("Accept-Encoding:", StringComparison.OrdinalIgnoreCase))
                    .Select(l => l["Accept-Encoding:".Length..].Trim())
                    .FirstOrDefault());

                byte[] bodyBytes = Gzip(Encoding.UTF8.GetBytes("hello-gzip"));
                string headerText2 = "HTTP/1.1 200 OK\r\n"
                    + "Content-Type: text/plain\r\n"
                    + "Content-Encoding: gzip\r\n"
                    + $"Content-Length: {bodyBytes.Length}\r\n"
                    + "Connection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headerText2));
                await stream.WriteAsync(bodyBytes);
            }
        }

        private static byte[] Gzip(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(data);
            }

            return output.ToArray();
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

    [Fact]
    public async Task SendAsync_自带AcceptEncoding头_原样到达且gzip响应解压()
    {
        using var server = new GzipHttpServer();
        using var factory = new HttpClientFactory();
        var engine = new HttpEngine(factory);
        SendRequest request = new("GET", $"http://127.0.0.1:{server.Port}/a",
            [new KeyValueEntry("Accept-Encoding", "gzip")], null, null);

        SendResult result = await engine.SendAsync(request, HermesSettings.Default);

        Assert.Equal("hello-gzip", result.FinalHop.Response.Body);
        // 不被 handler 并集成 "gzip, deflate, br"
        Assert.Equal("gzip", server.ReceivedAcceptEncoding.Single());
    }

    [Fact]
    public async Task SendAsync_未自带AcceptEncoding_不补头但压缩响应仍解压()
    {
        using var server = new GzipHttpServer();
        using var factory = new HttpClientFactory();
        var engine = new HttpEngine(factory);
        SendRequest request = new("GET", $"http://127.0.0.1:{server.Port}/a", [], null, null);

        SendResult result = await engine.SendAsync(request, HermesSettings.Default);

        Assert.Equal("hello-gzip", result.FinalHop.Response.Body);
        Assert.Null(server.ReceivedAcceptEncoding.Single());
    }
}
