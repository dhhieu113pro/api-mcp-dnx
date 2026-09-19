using System.Net;
using System.Net.Sockets;
using System.Text;
using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public sealed class HttpHelperTests
{
    [Fact]
    public void LargeResponseBody_IsTruncated()
    {
        using var server = new MiniHttpServer(new string('a', 150_000));

        var result = HttpHelper.Send("GET", server.Url, null, null, null, null, 10, true);

        Assert.Contains("Body (150,000 chars, showing first 100,000):", result);
        Assert.Contains("... body truncated (50,000 more chars)", result);
    }

    [Fact]
    public void SmallResponseBody_IsRenderedInFull()
    {
        using var server = new MiniHttpServer("hello world");

        var result = HttpHelper.Send("GET", server.Url, null, null, null, null, 10, true);

        Assert.Contains("Body (11 chars):", result);
        Assert.Contains("hello world", result);
        Assert.DoesNotContain("body truncated", result);
    }

    [Fact]
    public void QueryString_IsAppendedToUrl()
    {
        using var server = new MiniHttpServer("ok");
        HttpHelper.Send("GET", server.Url, null, "a=1&b=2", null, null, 10, true);

        Assert.Contains("?a=1&b=2", server.RequestLine ?? "");
    }

    [Fact]
    public void QueryMethod_IsTransmittedWithBody()
    {
        using var server = new MiniHttpServer("ok");
        HttpHelper.Send("QUERY", server.Url, null, null, "{\"probe\":1}", null, 10, true);

        Assert.Equal("QUERY", server.Method);
    }

    [Fact]
    public void Timeout_ReturnsTimeoutError()
    {
        using var server = new MiniHttpServer("slow", responseDelay: TimeSpan.FromSeconds(3));

        var result = HttpHelper.Send("GET", server.Url, null, null, null, null, 1, true);

        Assert.Contains("timed out after 1s", result);
    }

    [Fact]
    public void UnsupportedMethod_ReturnsError()
    {
        var result = HttpHelper.Send("TRACE", "https://example.test/", null, null, null, null, 10, true);

        Assert.Contains("unsupported HTTP method 'TRACE'", result);
    }

    [Fact]
    public void InvalidUrl_ReturnsError()
    {
        var result = HttpHelper.Send("GET", "not a url", null, null, null, null, 10, true);

        Assert.Contains("not a valid absolute URL", result);
    }

    [Fact]
    public void MissingUrl_ReturnsError()
    {
        var result = HttpHelper.Send("GET", "   ", null, null, null, null, 10, true);

        Assert.Contains("'url' is required.", result);
    }

    private sealed class MiniHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public string Url { get; }
        public volatile string? RequestLine;
        public volatile string? Method;

        public MiniHttpServer(string responseBody, TimeSpan? responseDelay = null)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Url = $"http://127.0.0.1:{port}/";
            _loop = Task.Run(() => ServeLoopAsync(responseBody, responseDelay, _cts.Token));
        }

        private async Task ServeLoopAsync(string body, TimeSpan? delay, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(client, body, delay, ct));
            }
        }

        private async Task HandleAsync(TcpClient client, string body, TimeSpan? delay, CancellationToken ct)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8, false, leaveOpen: true))
                {
                    var requestLine = await reader.ReadLineAsync(ct);
                    if (requestLine is null)
                        return;

                    RequestLine = requestLine;
                    Method = requestLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();

                    string? line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(ct)))
                    {
                    }

                    if (delay is not null)
                        await Task.Delay(delay.Value, ct);

                    var payload = Encoding.UTF8.GetBytes(body);
                    var headers = Encoding.UTF8.GetBytes(
                        $"HTTP/1.1 200 OK\r\n" +
                        $"Content-Type: text/plain\r\n" +
                        $"Content-Length: {payload.Length}\r\n" +
                        $"Connection: close\r\n" +
                        $"\r\n");

                    await stream.WriteAsync(headers, ct);
                    await stream.WriteAsync(payload, ct);
                }
            }
            catch (Exception)
            {
                // client disconnect / timeout / cancellation
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }
}