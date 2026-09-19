namespace ApiMcp.Helpers;

internal static class HttpHelper
{
    private const int MaxBodyChars = 100_000;

    private static readonly HashSet<string> AllowedMethods = new(
        ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "QUERY"],
        StringComparer.OrdinalIgnoreCase);

    // Headers that belong to HttpContent, not HttpRequestMessage.Headers.
    private static readonly HashSet<string> ContentHeaderNames = new(
        ["Content-Type", "Content-Length", "Content-Encoding", "Content-Language", "Content-Location",
         "Content-Range", "Content-MD5", "Content-Disposition", "Expires", "Last-Modified", "Allow"],
        StringComparer.OrdinalIgnoreCase);

    internal static string Send(
        string method,
        string url,
        string? headersJson,
        string? query,
        string? body,
        string? multipartJson,
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

            if (!string.IsNullOrWhiteSpace(multipartJson))
            {
                // Multipart sets its own boundary; ignore any user-supplied Content-Type.
                ApplyMultipart(request, multipartJson);
                ApplyHeaders(request, headersJson, skipContentType: true);
            }
            else
            {
                // Body first so content headers (e.g. Content-Type) have an HttpContent to attach to.
                ApplyBody(request, body, headersJson);
                ApplyHeaders(request, headersJson);
            }

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
        var processed = QueryParamStore.Apply(query.Trim().TrimStart('?'));
        return url + separator + processed;
    }

    private static void ApplyHeaders(HttpRequestMessage request, string? headersJson, bool skipContentType = false)
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
                if (skipContentType && ContentHeaderNames.Contains(prop.Name))
                    continue;

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

                if (ContentHeaderNames.Contains(prop.Name))
                {
                    request.Content ??= new System.Net.Http.StringContent("");
                    request.Content.Headers.Remove(prop.Name);
                    request.Content.Headers.TryAddWithoutValidation(prop.Name, resolved);
                }
                else
                {
                    request.Headers.Remove(prop.Name);
                    request.Headers.TryAddWithoutValidation(prop.Name, resolved);
                }
            }
        }
    }

    private static void ApplyMultipart(HttpRequestMessage request, string multipartJson)
    {
        System.Text.Json.JsonDocument doc;
        try
        {
            doc = System.Text.Json.JsonDocument.Parse(multipartJson);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException("'multipart' must be a JSON object like {\"fields\":{...},\"files\":[{...}]}.", ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new InvalidOperationException("'multipart' must be a JSON object.");

            var form = new System.Net.Http.MultipartFormDataContent();
            int count = 0;

            if (doc.RootElement.TryGetProperty("fields", out var fields))
            {
                if (fields.ValueKind != System.Text.Json.JsonValueKind.Object)
                    throw new InvalidOperationException("'multipart.fields' must be a JSON object.");
                foreach (var f in fields.EnumerateObject())
                {
                    var value = f.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? f.Value.GetString()!
                        : f.Value.GetRawText();
                    form.Add(new System.Net.Http.StringContent(value), f.Name);
                    count++;
                }
            }

            if (doc.RootElement.TryGetProperty("files", out var files))
            {
                if (files.ValueKind != System.Text.Json.JsonValueKind.Array)
                    throw new InvalidOperationException("'multipart.files' must be a JSON array.");
                foreach (var file in files.EnumerateArray())
                {
                    AddFile(form, file);
                    count++;
                }
            }

            if (count == 0)
                throw new InvalidOperationException("'multipart' must contain at least one field or file.");

            request.Content = form;
        }
    }

    private static void AddFile(System.Net.Http.MultipartFormDataContent form, System.Text.Json.JsonElement file)
    {
        if (file.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new InvalidOperationException("Each 'multipart.files' item must be a JSON object.");

        var field = GetString(file, "field") ?? "file";
        var fileName = GetString(file, "fileName");
        var contentType = GetString(file, "contentType");
        var path = GetString(file, "path");
        var base64 = GetString(file, "contentBase64");
        var base64Url = GetString(file, "contentBase64Url");

        byte[] bytes;
        if (!string.IsNullOrEmpty(path))
        {
            var full = FileAccessPolicy.Resolve(path);
            fileName ??= Path.GetFileName(full);
            bytes = File.ReadAllBytes(full);
        }
        else if (!string.IsNullOrEmpty(base64) || !string.IsNullOrEmpty(base64Url))
        {
            var payload = base64 ?? base64Url!;
            if (!string.IsNullOrEmpty(base64Url))
                payload = base64Url.Replace('-', '+').Replace('_', '/');
            try
            {
                bytes = Convert.FromBase64String(payload);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"File '{field}': base64 content is not valid.", ex);
            }
            fileName ??= field;
        }
        else
        {
            throw new InvalidOperationException($"File '{field}' needs either 'path' or 'contentBase64'.");
        }

        contentType ??= GuessContentTypeFromName(fileName);
        var content = new System.Net.Http.ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(content, field, fileName);
    }

    private static string? GetString(System.Text.Json.JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString()
            : null;

    private static string GuessContentTypeFromName(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" or ".log" => "text/plain",
            ".csv" => "text/csv",
            ".html" or ".htm" => "text/html",
            ".zip" => "application/zip",
            ".gz" => "application/gzip",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            ".wav" => "audio/wav",
            ".webm" => "video/webm",
            _ => "application/octet-stream",
        };
    }

    private static void ApplyBody(HttpRequestMessage request, string? body, string? headersJson)
    {
        if (string.IsNullOrEmpty(body))
            return;

        var contentType = ResolveContentType(headersJson) ?? GuessContentType(body);

        request.Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
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