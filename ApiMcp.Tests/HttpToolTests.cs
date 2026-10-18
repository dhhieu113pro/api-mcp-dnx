using System.Text.Json;
using ApiMcp.AI.Tools;
using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public sealed class HttpToolTests : IDisposable
{
    private const string HeaderTokenEnv = "APIMCP_TOOLTEST_AUTH";
    private readonly Dictionary<string, string?> _originalEnv = new();

    public HttpToolTests()
    {
        HeaderStore.Clear();
        foreach (var name in new[] { HeaderTokenEnv })
        {
            _originalEnv[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        HeaderStore.Clear();
        foreach (var pair in _originalEnv)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void HttpRequest_WithoutExtras_PerformsGet()
    {
        using var server = new MiniHttpServer("ok");
        var tool = new HttpTool();

        var result = tool.HttpRequest("GET", server.Url);

        Assert.Contains("HTTP 200", result);
        Assert.Contains("ok", result);
    }

    [Fact]
    public void HttpRequest_WithJsonObjectArguments_IsSerializedAndSent()
    {
        using var server = new MiniHttpServer("ok");
        var tool = new HttpTool();

        var result = tool.HttpRequest(
            "POST",
            server.Url,
            headers: Json("{\"Content-Type\":\"application/json\",\"X-Custom\":\"yes\"}"),
            query: "page=2",
            body: "{\"a\":1}",
            timeoutSeconds: 10,
            followRedirects: true);

        Assert.Contains("HTTP 200", result);
        Assert.Equal("POST", server.Method);
        Assert.Contains("?page=2", server.RequestLine ?? "");
        Assert.Contains("X-Custom: yes", server.RequestHeaders ?? "");
        Assert.Equal("{\"a\":1}", server.RequestBodyText);
    }

    [Fact]
    public void HttpRequest_WithMultipartJsonObject_IsSent()
    {
        using var server = new MiniHttpServer("ok");
        var tool = new HttpTool();
        var multipart = Json("{\"fields\":{\"name\":\"value\"}}");

        var result = tool.HttpRequest("POST", server.Url, multipart: multipart);

        Assert.Contains("HTTP 200", result);
        Assert.Contains("multipart/form-data; boundary=", server.RequestHeaders ?? "");
    }

    [Fact]
    public void HttpRequest_InvalidUrl_ReturnsError()
    {
        var tool = new HttpTool();

        var result = tool.HttpRequest("GET", "not-a-url");

        Assert.Contains("not a valid absolute URL", result);
    }

    [Fact]
    public void HttpRequest_WithSelect_ReturnsProjectedBody()
    {
        using var server = new MiniHttpServer("{\"a\":1,\"b\":2}");
        var tool = new HttpTool();

        var result = tool.HttpRequest("GET", server.Url, select: "$.a");

        Assert.Contains("Selected ($.a)", result);
        Assert.DoesNotContain("\"b\"", result);
    }

    [Fact]
    public void HttpRequest_WithCapture_ReportsCaptured()
    {
        using var server = new MiniHttpServer("{\"token\":\"t1\"}", responseContentType: "application/json");
        var tool = new HttpTool();

        var result = tool.HttpRequest("GET", server.Url, capture: JsonDocument.Parse("{\"vt_tool\":\"$.token\"}").RootElement);

        Assert.Contains("Captured:", result);
        Assert.Contains("vt_tool = t1", result);
    }

    [Fact]
    public void HttpRequest_WithAssertions_ReportsPass()
    {
        using var server = new MiniHttpServer("ok");
        var tool = new HttpTool();

        var result = tool.HttpRequest("GET", server.Url, expectStatus: 200, expectBodyContains: "ok");

        Assert.Contains("[PASS] assertions (2/2)", result);
        Assert.Contains("HTTP 200", result);
    }

    [Fact]
    public void HttpRequest_AssertionFailure_ReportsFail()
    {
        using var server = new MiniHttpServer("ok");
        var tool = new HttpTool();

        var result = tool.HttpRequest("GET", server.Url, expectStatus: 201);

        Assert.Contains("[FAIL] assertions (0/1)", result);
    }
}