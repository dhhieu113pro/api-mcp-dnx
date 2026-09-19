using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ApiMcp.Tests;

internal sealed class MiniHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public string Url { get; }
    public volatile string? RequestLine;
    public volatile string? Method;
    public volatile string? RequestHeaders;
    public volatile byte[]? RequestBody;
    public volatile int Connections;
    public readonly System.Collections.Concurrent.ConcurrentQueue<string> RequestTraces = new();

    public MiniHttpServer(
        string responseBody = "ok",
        TimeSpan? responseDelay = null,
        int statusCode = 200,
        string? reasonPhrase = "OK",
        bool sendResponse = true,
        string? responseContentType = "text/plain")
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Url = $"http://127.0.0.1:{port}/";
        _loop = Task.Run(() => ServeLoopAsync(responseBody, responseDelay, statusCode, reasonPhrase, sendResponse, responseContentType, _cts.Token));
    }

    public string? RequestBodyText => RequestBody is null ? null : Encoding.UTF8.GetString(RequestBody);

    private async Task ServeLoopAsync(string body, TimeSpan? delay, int statusCode, string? reasonPhrase, bool sendResponse, string? responseContentType, CancellationToken ct)
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

            _ = Task.Run(() => HandleAsync(client, body, delay, statusCode, reasonPhrase, sendResponse, responseContentType, ct));
        }
    }

    private async Task HandleAsync(TcpClient client, string body, TimeSpan? delay, int statusCode, string? reasonPhrase, bool sendResponse, string? responseContentType, CancellationToken ct)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var id = Interlocked.Increment(ref Connections);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                RequestTraces.Enqueue($"#{id} connect");
                var headerBytes = await ReadUntilHeaderEndAsync(stream, ct);
                if (headerBytes.Length == 0)
                    return;

                var headers = Encoding.UTF8.GetString(headerBytes);
                RequestHeaders = headers;

                var lines = headers.Replace("\r\n", "\n").Split('\n');
                var first = lines.FirstOrDefault() ?? "";
                RequestLine = first;
                Method = first.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

                var contentLength = 0L;
                foreach (var line in lines)
                {
                    var idx = line.IndexOf(':');
                    if (idx > 0 && string.Equals(line[..idx].Trim(), "Content-Length", StringComparison.OrdinalIgnoreCase))
                        long.TryParse(line[(idx + 1)..].Trim(), out contentLength);
                }

                if (contentLength > 0)
                {
                    var buffer = new byte[contentLength];
                    var read = 0;
                    while (read < contentLength)
                    {
                        var n = await stream.ReadAsync(buffer.AsMemory(read, (int)(contentLength - read)), ct);
                        if (n == 0)
                            break;
                        read += n;
                    }
RequestBody = buffer[..read];
                RequestTraces.Enqueue($"#{id} body:{read}/{contentLength}@{sw.ElapsedMilliseconds}ms");
            }
            else
            {
                RequestTraces.Enqueue($"#{id} noBody@{sw.ElapsedMilliseconds}ms");
            }

                if (delay is not null)
                    await Task.Delay(delay.Value, ct);

                if (!sendResponse)
                    return;

                var payload = Encoding.UTF8.GetBytes(body);
                var statusLine = string.IsNullOrEmpty(reasonPhrase)
                    ? $"HTTP/1.1 {statusCode}\r\n"
                    : $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n";
                var responseHeaders = Encoding.UTF8.GetBytes(
                    statusLine +
                    $"Content-Type: {responseContentType}\r\n" +
                    $"Content-Length: {payload.Length}\r\n" +
                    $"Connection: close\r\n" +
                    $"\r\n");

                await stream.WriteAsync(responseHeaders, ct);
                await stream.WriteAsync(payload, ct);
            }
        }
        catch (Exception)
        {
            // client disconnect / timeout / cancellation
        }
    }

    private static async Task<byte[]> ReadUntilHeaderEndAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var tail = new byte[4];
        var filled = 0;
        while (true)
        {
            var buf = new byte[1];
            var n = await stream.ReadAsync(buf.AsMemory(0, 1), ct);
            if (n == 0)
                break;
            var b = buf[0];
            ms.WriteByte(b);
            tail[filled % 4] = b;
            filled++;
            if (filled >= 4 &&
                tail[(filled - 4) % 4] == (byte)'\r' && tail[(filled - 3) % 4] == (byte)'\n' &&
                tail[(filled - 2) % 4] == (byte)'\r' && tail[(filled - 1) % 4] == (byte)'\n')
                break;
        }
        return ms.ToArray();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}