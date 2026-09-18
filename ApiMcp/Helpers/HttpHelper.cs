namespace ApiMcp.Helpers;

internal static class HttpHelper
{
    private const int MaxBodyChars = 100_000;

    private static readonly HashSet<string> AllowedMethods = new(
        ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"],
        StringComparer.OrdinalIgnoreCase);

    internal static string Send(
        string method,
        string url,
        string? headersJson,
        string? query,
        string? body,
        int timeoutSeconds,
        bool followRedirects)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
                return "Error: 'url' is required.";

            method = method.Trim().ToUpperInvariant();
            if (!AllowedMethods.Contains(method))
                return $"Error: unsupported HTTP method '{method}'. Allowed: {string.Join(", ", AllowedMethods.OrderBy(m => m))}.";

            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                return $"Error: '{url}' is not a valid absolute URL.";

            url = ApplyQueryString(url, query);

            using var client = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = followRedirects,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli,
            });
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));

            using var request = new HttpRequestMessage(new HttpMethod(method), url);

            ApplyHeaders(request, headersJson);
            ApplyBody(request, body, headersJson);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var response = client.Send(request, HttpCompletionOption.ResponseContentRead);
            sw.Stop();

            return RenderResponse(response, sw.ElapsedMilliseconds);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return $"Error: request timed out after {timeoutSeconds}s.";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: HTTP request failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Unexpected error: {ex.Message}";
        }
    }

    private static string ApplyQueryString(string url, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return url;

        var separator = url.Contains('?') ? "&" : "?";
        return url + separator + query.Trim().TrimStart('?');
    }

    private static void ApplyHeaders(HttpRequestMessage request, string? headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson))
            return;

        System.Text.Json.JsonDocument? doc = null;
        try
        {
            doc = System.Text.Json.JsonDocument.Parse(headersJson);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException($"'headers' must be a JSON object like {{\"Content-Type\":\"application/json\"}}: {ex.Message}", ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new InvalidOperationException("'headers' must be a JSON object.");

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Null)
                {
                    if (!HeaderStore.IsSecret(prop.Name))
                        throw new InvalidOperationException($"Header '{prop.Name}' has no value and no secret mapping is configured for it.");
                    continue;
                }
                if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String)
                    throw new InvalidOperationException($"Header '{prop.Name}' value must be a JSON string.");

                var resolved = HeaderStore.Resolve(prop.Name, prop.Value.GetString());
                if (string.IsNullOrEmpty(resolved))
                    continue;
                request.Headers.Remove(prop.Name);
                if (!request.Headers.TryAddWithoutValidation(prop.Name, resolved))
                {
                    request.Content ??= new System.Net.Http.StringContent("");
                    var ok = request.Content.Headers.TryAddWithoutValidation(prop.Name, resolved);
                    if (!ok)
                        throw new InvalidOperationException($"Unable to set header '{prop.Name}'.");
                }
            }
        }
    }

    private static void ApplyBody(HttpRequestMessage request, string? body, string? headersJson)
    {
        if (string.IsNullOrEmpty(body))
            return;

        var contentType = ResolveContentType(headersJson) ?? GuessContentType(body);

        request.Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        if (body is { Length: > 0 })
            request.Content.Headers.ContentLength = System.Text.Encoding.UTF8.GetByteCount(body);
    }

    private static string? ResolveContentType(string? headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson))
            return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(headersJson);
            if (doc.RootElement.TryGetProperty("Content-Type", out var ct))
                return ct.ValueKind == System.Text.Json.JsonValueKind.String ? ct.GetString() : null;
            if (doc.RootElement.TryGetProperty("content-type", out var low))
                return low.ValueKind == System.Text.Json.JsonValueKind.String ? low.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            // non-JSON headers string; ApplyBody is called after ApplyHeaders already failed anyway
        }
        return null;
    }

    private static string GuessContentType(string body)
    {
        var t = body.TrimStart();
        if (t.StartsWith('{') || t.StartsWith('['))
            return "application/json";
        if (t.StartsWith('<'))
            return "application/xml";
        return "text/plain";
    }

    private static string RenderResponse(HttpResponseMessage response, long elapsedMs)
    {
        var sb = new System.Text.StringBuilder();

        var reason = string.IsNullOrEmpty(response.ReasonPhrase) ? "" : $" {response.ReasonPhrase}";
        sb.AppendLine($"HTTP {(int)response.StatusCode} {response.StatusCode}{reason} ({elapsedMs}ms)");

        sb.AppendLine("Response Headers:");
        foreach (var h in response.Headers)
            foreach (var v in h.Value)
                sb.AppendLine($"  {h.Key}: {v}");
        if (response.Content.Headers is { } ch)
            foreach (var h in ch)
                foreach (var v in h.Value)
                    sb.AppendLine($"  {h.Key}: {v}");

        var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (content.Length > MaxBodyChars)
        {
            sb.AppendLine();
            sb.AppendLine($"Body ({content.Length:N0} chars, showing first {MaxBodyChars:N0}):");
            sb.AppendLine(content[..MaxBodyChars]);
            sb.AppendLine();
            sb.AppendLine($"... body truncated ({content.Length - MaxBodyChars:N0} more chars)");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine($"Body ({content.Length:N0} chars):");
            sb.AppendLine(content);
        }

        return sb.ToString();
    }
}