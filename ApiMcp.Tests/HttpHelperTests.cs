using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public sealed class HttpHelperTests
{
    private const string HeaderTokenEnv = "APIMCP_HTTPTEST_AUTH_TOKEN";
    private const string HeaderKeyEnv = "APIMCP_HTTPTEST_API_KEY";

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
    public void QueryString_IsAppendedToExistingQuery()
    {
        using var server = new MiniHttpServer("ok");
        HttpHelper.Send("GET", server.Url + "?a=1", null, "b=2", null, null, 10, true);

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

    [Fact]
    public async Task SecretQueryParam_IsInjectedOverTheWire()
    {
        const string tokenEnv = "APIMCP_HTTPTEST_QUERY_TOKEN";
        var original = Environment.GetEnvironmentVariable(tokenEnv);
        try
        {
            Environment.SetEnvironmentVariable(tokenEnv, "wire secret+/");
            QueryParamStore.AddMapping("api_key", tokenEnv);

            using var server = new MiniHttpServer("ok");
            HttpHelper.Send("GET", server.Url, null, "api_key=__IGNORED__&page=1", null, null, 10, true);

            var requestLine = server.RequestLine;
            for (var i = 0; i < 50 && requestLine is null; i++)
            {
                await Task.Delay(50);
                requestLine = server.RequestLine;
            }

            Assert.Contains("api_key=wire%20secret%2B%2F&page=1", requestLine ?? "");
            Assert.DoesNotContain("__IGNORED__", requestLine ?? "");
        }
        finally
        {
            Environment.SetEnvironmentVariable(tokenEnv, original);
            QueryParamStore.Clear();
        }
    }

    [Fact]
    public void BodyAndHeaders_AreSentOverTheWire()
    {
        using var server = new MiniHttpServer("ok");
        const string body = "{\"a\":1}";

        HttpHelper.Send("POST", server.Url,
            "{\"Content-Type\":\"application/json\",\"X-Custom\":\"yes\"}",
            null, body, null, 10, true);

        Assert.Equal("POST", server.Method);
        Assert.Equal(body, server.RequestBodyText);
        Assert.Contains("Content-Type: application/json", server.RequestHeaders ?? "");
        Assert.Contains("X-Custom: yes", server.RequestHeaders ?? "");
        Assert.Contains("Content-Length: 7", server.RequestHeaders ?? "");
    }

    [Fact]
    public void Headers_InvalidJson_ReturnsUnexpectedError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("GET", server.Url, "not-a-json", null, null, null, 10, true);

        Assert.Contains("Unexpected error:", result);
        Assert.Contains("'headers' must be a JSON object like", result);
    }

    [Fact]
    public void Headers_NotAnObject_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("GET", server.Url, "[1,2]", null, null, null, 10, true);

        Assert.Contains("'headers' must be a JSON object.", result);
    }

    [Fact]
    public void Headers_NullValueNonSecret_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");
        HeaderStore.Clear();

        var result = HttpHelper.Send("POST", server.Url, "{\"X-Empty\":null}", null, "x", null, 10, true);

        Assert.Contains("Header 'X-Empty' has no value and no secret mapping is configured for it.", result);
    }

    [Fact]
    public void Headers_NullValueOnSecret_IsSkipped()
    {
        var original = Environment.GetEnvironmentVariable(HeaderKeyEnv);
        try
        {
            Environment.SetEnvironmentVariable(HeaderKeyEnv, "s3cr3t");
            HeaderStore.Clear();
            HeaderStore.AddMapping("X-Api-Key", HeaderKeyEnv);

            using var server = new MiniHttpServer("ok");
            var result = HttpHelper.Send("POST", server.Url, "{\"X-Api-Key\":null}", null, "x", null, 10, true);

            Assert.Contains("HTTP 200", result);
            Assert.DoesNotContain("X-Api-Key", server.RequestHeaders ?? "");
        }
        finally
        {
            Environment.SetEnvironmentVariable(HeaderKeyEnv, original);
            HeaderStore.Clear();
        }
    }

    [Fact]
    public void Headers_NonStringValue_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("GET", server.Url, "{\"X-Int\":5}", null, null, null, 10, true);

        Assert.Contains("Header 'X-Int' value must be a JSON string.", result);
    }

    [Fact]
    public void Headers_SecretWinsOverLlmValue()
    {
        var original = Environment.GetEnvironmentVariable(HeaderTokenEnv);
        try
        {
            Environment.SetEnvironmentVariable(HeaderTokenEnv, "the-real-token");
            HeaderStore.Clear();
            HeaderStore.AddMapping("Authorization", HeaderTokenEnv);

            using var server = new MiniHttpServer("ok");
            HttpHelper.Send("POST", server.Url, "{\"Authorization\":\"forged\"}", null, "x", null, 10, true);

            Assert.Contains("Authorization: the-real-token", server.RequestHeaders ?? "");
            Assert.DoesNotContain("forged", server.RequestHeaders ?? "");
        }
        finally
        {
            Environment.SetEnvironmentVariable(HeaderTokenEnv, original);
            HeaderStore.Clear();
        }
    }

    [Fact]
    public void Headers_EmptyValue_IsSkipped()
    {
        using var server = new MiniHttpServer("ok");

        HttpHelper.Send("GET", server.Url, "{\"X-Skip\":\"\"}", null, null, null, 10, true);

        Assert.DoesNotContain("X-Skip", server.RequestHeaders ?? "");
    }

    [Fact]
    public void Headers_InvalidRequestHeaderName_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("GET", server.Url, "{\"Bad Header\":\"x\"}", null, null, null, 10, true);

        Assert.Contains("The header name 'Bad Header'", result);
    }

    [Fact]
    public void Headers_ContentTypeWithoutBody_AttachesToContent()
    {
        using var server = new MiniHttpServer("ok");

        HttpHelper.Send("GET", server.Url, "{\"Content-Type\":\"application/json\"}", null, null, null, 10, true);

        Assert.Contains("Content-Type: application/json", server.RequestHeaders ?? "");
    }

    [Fact]
    public void Multipart_FieldsAndBase64File_AreSent()
    {
        using var server = new MiniHttpServer("ok");

        const string multipart = """
        {"fields":{"name":"value"},"files":[{"field":"file","fileName":"a.txt","contentBase64":"aGVsbG8="}]}
        """;

        var result = HttpHelper.Send("POST", server.Url, null, null, null, multipart, 10, true);
        var headers = server.RequestHeaders ?? "";
        var body = server.RequestBodyText ?? "";

        Assert.Contains("HTTP 200", result);
        Assert.Contains("multipart/form-data; boundary=", headers);
        Assert.Contains("name=name", body);
        Assert.Contains("value", body);
        Assert.Contains("filename=a.txt", body);
        Assert.Contains("hello", body);
        Assert.DoesNotContain("application/json", headers);
    }

    [Fact]
    public void Multipart_ContentTypeHeader_IsIgnored()
    {
        using var server = new MiniHttpServer("ok");

        const string multipart = """
        {"fields":{"a":"b"}}
        """;

        HttpHelper.Send("POST", server.Url, "{\"Content-Type\":\"application/json\"}", null, null, multipart, 10, true);

        Assert.Contains("multipart/form-data; boundary=", server.RequestHeaders ?? "");
    }

    [Fact]
    public void Multipart_NotJson_ReturnsUnexpectedError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("POST", server.Url, null, null, null, "nope", 10, true);

        Assert.Contains("'multipart' must be a JSON object like", result);
    }

    [Fact]
    public void Multipart_NotAnObject_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("POST", server.Url, null, null, null, "[]", 10, true);

        Assert.Contains("'multipart' must be a JSON object.", result);
    }

    [Fact]
    public void Multipart_FieldsNotObject_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("POST", server.Url, null, null, null, "{\"fields\":[]}", 10, true);

        Assert.Contains("'multipart.fields' must be a JSON object.", result);
    }

    [Fact]
    public void Multipart_FilesNotArray_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("POST", server.Url, null, null, null, "{\"fields\":{\"a\":\"b\"},\"files\":{}}", 10, true);

        Assert.Contains("'multipart.files' must be a JSON array.", result);
    }

    [Fact]
    public void Multipart_Empty_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("POST", server.Url, null, null, null, "{}", 10, true);

        Assert.Contains("'multipart' must contain at least one field or file.", result);
    }

    [Fact]
    public void Multipart_NonStringFieldValue_IsSerializedRaw()
    {
        using var server = new MiniHttpServer("ok");

        HttpHelper.Send("POST", server.Url, null, null, null, "{\"fields\":{\"n\":5}}", 10, true);

        var body = server.RequestBodyText ?? "";
        Assert.Contains("name=n", body);
        Assert.Contains("\r\n5\r\n", body);
    }

    [Fact]
    public void AddFile_NonObjectItem_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("POST", server.Url, null, null, null, "{\"files\":[5]}", 10, true);

        Assert.Contains("Each 'multipart.files' item must be a JSON object.", result);
    }

    [Fact]
    public void AddFile_ByPath_SendsFileContent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"apimcp-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "file-content-123");
        try
        {
using var server = new MiniHttpServer("ok");
        var multipart = JsonSerializer.Serialize(new
        {
            files = new object[] { new { field = "upload", path = tempFile } }
        });

        var result = HttpHelper.Send("POST", server.Url, null, null, null, multipart, 10, true);

        var body = server.RequestBodyText ?? "";
        Assert.True(
            body.Contains("file-content-123") && body.Contains($"filename={Path.GetFileName(tempFile)}"),
            $"sendResult=[{result}] requestBody=[{body}] connections={server.Connections} trace=[{string.Join(" | ", server.RequestTraces)}]");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void AddFile_PathMissing_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");
        var missing = Path.Combine(Path.GetTempPath(), "apimcp-does-not-exist-" + Guid.NewGuid().ToString("N") + ".bin");
        var multipart = JsonSerializer.Serialize(new
        {
            files = new object[] { new { path = missing } }
        });

        var result = HttpHelper.Send("POST", server.Url, null, null, null, multipart, 10, true);

        Assert.Contains("File not found", result);
    }

    [Fact]
    public void AddFile_InvalidBase64_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("POST", server.Url, null, null, null,
            "{\"files\":[{\"field\":\"f\",\"contentBase64\":\"!!!\"}]}", 10, true);

        Assert.Contains("base64 content is not valid.", result);
    }

    [Fact]
    public void AddFile_NeitherPathNorBase64_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("POST", server.Url, null, null, null,
            "{\"files\":[{\"field\":\"x\"}]}", 10, true);

        Assert.Contains("File 'x' needs either 'path' or 'contentBase64'.", result);
    }

    [Fact]
    public void AddFile_ContentBase64Url_IsDecoded()
    {
        using var server = new MiniHttpServer("ok");
        var bytes = new byte[] { 0x01, 0x02, 0xfe, 0xff, 0x00, 0x7f };
        var base64Url = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        HttpHelper.Send("POST", server.Url, null, null, null,
            $"{{\"files\":[{{\"field\":\"bin\",\"contentBase64Url\":\"{base64Url}\"}}]}}", 10, true);

        Assert.Contains("name=bin", server.RequestBodyText ?? "");
        Assert.True(ContainsSequence(server.RequestBody ?? [], bytes), "multipart body should contain the decoded file bytes");
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return true;
        }
        return false;
    }

    [Theory]
    [InlineData("photo.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("anim.gif", "image/gif")]
    [InlineData("pic.webp", "image/webp")]
    [InlineData("pic.bmp", "image/bmp")]
    [InlineData("pic.svg", "image/svg+xml")]
    [InlineData("doc.pdf", "application/pdf")]
    [InlineData("data.json", "application/json")]
    [InlineData("doc.xml", "application/xml")]
    [InlineData("readme.txt", "text/plain")]
    [InlineData("app.log", "text/plain")]
    [InlineData("rows.csv", "text/csv")]
    [InlineData("page.html", "text/html")]
    [InlineData("page.htm", "text/html")]
    [InlineData("bundle.zip", "application/zip")]
    [InlineData("file.gz", "application/gzip")]
    [InlineData("file.doc", "application/msword")]
    [InlineData("file.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("file.xls", "application/vnd.ms-excel")]
    [InlineData("file.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("p.ppt", "application/vnd.ms-powerpoint")]
    [InlineData("p.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("song.mp3", "audio/mpeg")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("sound.wav", "audio/wav")]
    [InlineData("movie.webm", "video/webm")]
    [InlineData("unknown.bin", "application/octet-stream")]
    [InlineData("noextension", "application/octet-stream")]
    public void Multipart_FileContentType_IsGuessedFromFileName(string fileName, string expectedContentType)
    {
        using var server = new MiniHttpServer("ok");
        var multipart = $"{{\"files\":[{{\"field\":\"upload\",\"fileName\":\"{fileName}\",\"contentBase64\":\"aGVsbG8=\"}}]}}";

        HttpHelper.Send("POST", server.Url, null, null, null, multipart, 10, true);

        Assert.Contains($"Content-Type: {expectedContentType}", server.RequestBodyText ?? "");
    }

    [Theory]
    [InlineData("{\"a\":1}", "application/json")]
    [InlineData("[1,2]", "application/json")]
    [InlineData("<soap/>", "application/xml")]
    [InlineData("plain text", "text/plain")]
    public void Body_ContentType_IsGuessedFromShape(string body, string expectedContentType)
    {
        using var server = new MiniHttpServer("ok");

        HttpHelper.Send("POST", server.Url, null, null, body, null, 10, true);

        Assert.Contains($"Content-Type: {expectedContentType}", server.RequestHeaders ?? "");
    }

    [Fact]
    public void Body_ExplicitContentTypeHeader_Wins()
    {
        using var server = new MiniHttpServer("ok");

        HttpHelper.Send("POST", server.Url, "{\"Content-Type\":\"application/x-www-form-urlencoded\"}", null, "a=1", null, 10, true);

        Assert.Contains("Content-Type: application/x-www-form-urlencoded", server.RequestHeaders ?? "");
    }

    [Fact]
    public void Body_LowerCaseContentTypeHeader_Wins()
    {
        using var server = new MiniHttpServer("ok");

        HttpHelper.Send("POST", server.Url, "{\"content-type\":\"text/csv\"}", null, "a,b", null, 10, true);

        Assert.Contains("Content-Type: text/csv", server.RequestHeaders ?? "");
    }

    [Fact]
    public void Body_NonStringContentType_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("POST", server.Url, "{\"Content-Type\":5}", null, "{\"a\":1}", null, 10, true);

        Assert.Contains("Header 'Content-Type' value must be a JSON string.", result);
    }

    [Fact]
    public void Body_LowerCaseNonStringContentType_ReturnsError()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("POST", server.Url, "{\"content-type\":5}", null, "{\"a\":1}", null, 10, true);

        Assert.Contains("Header 'content-type' value must be a JSON string.", result);
    }

    [Fact]
    public void Body_InvalidHeadersJson_CleansThenGuesses()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("POST", server.Url, "{oops", null, "{\"a\":1}", null, 10, true);

        Assert.Contains("'headers' must be a JSON object like", result);
    }

    [Fact]
    public void RefusedConnection_ReturnsHttpRequestError()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var result = HttpHelper.Send("GET", $"http://127.0.0.1:{port}/", null, null, null, null, 10, true);

        Assert.Contains("HTTP request failed", result);
    }

    [Fact]
    public void EmptyReasonPhrase_IsRenderedWithoutTrailingGap()
    {
        using var server = new MiniHttpServer("ok", reasonPhrase: "");

        var result = HttpHelper.Send("GET", server.Url, null, null, null, null, 10, true);

        Assert.Contains("HTTP 200 OK (", result);
    }

    [Fact]
    public void Responses_WithHeaders_AreRendered()
    {
        using var server = new MiniHttpServer("ok");

        var result = HttpHelper.Send("GET", server.Url, null, null, null, null, 10, true);

        Assert.Contains("Response Headers:", result);
        Assert.Contains("Content-Type: text/plain", result);
    }
}