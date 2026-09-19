using System.Text.Json;
using ApiMcp.AI.Tools;
using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public sealed class OpenApiToolTests : IDisposable
{
    private const string HeaderTokenEnv = "APIMCP_OPENTOOL_AUTH";
    private readonly string? _originalEnv;

    public OpenApiToolTests()
    {
        HeaderStore.Clear();
        _originalEnv = Environment.GetEnvironmentVariable(HeaderTokenEnv);
        Environment.SetEnvironmentVariable(HeaderTokenEnv, null);
    }

    public void Dispose()
    {
        HeaderStore.Clear();
        Environment.SetEnvironmentVariable(HeaderTokenEnv, _originalEnv);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private const string MiniSpec =
        """
        openapi: 3.0.3
        info:
          title: Mini API
          version: 1.0.0
        paths:
          /ping:
            get:
              responses:
                '200':
                  description: OK
        """;

    [Fact]
    public void ParseOpenApi_WithSpecification_ReturnsEndpointsJson()
    {
        var tool = new OpenApiTool();

        var result = tool.ParseOpenApi(specification: MiniSpec);

        Assert.DoesNotContain("Error", result);
        Assert.Contains("\"method\": \"GET\"", result);
        Assert.Contains("\"/ping\"", result);
    }

    [Fact]
    public void ParseOpenApi_Url_FetchesAndParsesDocument()
    {
        using var server = new MiniHttpServer(MiniSpec);
        var tool = new OpenApiTool();

        var result = tool.ParseOpenApi(url: server.Url);

        Assert.DoesNotContain("Error", result);
        Assert.Contains("\"method\": \"GET\"", result);
        Assert.Contains("\"/ping\"", result);
    }

    [Fact]
    public void ParseOpenApi_BothProvided_PrefersSpecification()
    {
        using var server = new MiniHttpServer("this is not openapi");
        var tool = new OpenApiTool();

        var result = tool.ParseOpenApi(url: server.Url, specification: MiniSpec);

        Assert.DoesNotContain("Error", result);
        Assert.Contains("\"/ping\"", result);
    }

    [Fact]
    public void ParseOpenApi_NeitherProvided_ReturnsError()
    {
        var tool = new OpenApiTool();

        var result = tool.ParseOpenApi();

        Assert.Contains("Provide either 'url' or 'specification'.", result);
    }

    [Fact]
    public void ParseOpenApi_InvalidUrl_ReturnsError()
    {
        var tool = new OpenApiTool();

        var result = tool.ParseOpenApi(url: "not-a-url");

        Assert.Contains("is not a valid absolute URL", result);
    }

    [Fact]
    public void ParseOpenApi_UrlReturns404_ReturnsError()
    {
        using var server = new MiniHttpServer("nope", statusCode: 404, reasonPhrase: "Not Found");
        var tool = new OpenApiTool();

        var result = tool.ParseOpenApi(url: server.Url);

        Assert.Contains("returned HTTP 404", result);
    }

    [Fact]
    public void ParseOpenApi_UrlReturnsEmpty_ReturnsError()
    {
        using var server = new MiniHttpServer("");
        var tool = new OpenApiTool();

        var result = tool.ParseOpenApi(url: server.Url);

        Assert.Contains("returned an empty document", result);
    }

    [Fact]
    public void ParseOpenApi_UrlWithSecretHeader_ForwardsResolvedValue()
    {
        Environment.SetEnvironmentVariable(HeaderTokenEnv, "env-secret-value");
        HeaderStore.AddMapping("Authorization", HeaderTokenEnv);
        using var server = new MiniHttpServer(MiniSpec);
        var tool = new OpenApiTool();

        tool.ParseOpenApi(url: server.Url, headers: Json("{\"Authorization\":\"forged-value\"}"));

        Assert.Contains("Authorization: env-secret-value", server.RequestHeaders ?? "");
    }

    [Fact]
    public void ParseOpenApi_UrlWithUnmappedHeader_ForwardsLiteralValue()
    {
        using var server = new MiniHttpServer(MiniSpec);
        var tool = new OpenApiTool();

        tool.ParseOpenApi(url: server.Url, headers: Json("{\"X-Custom\":\"literal\"}"));

        Assert.Contains("X-Custom: literal", server.RequestHeaders ?? "");
    }

    [Fact]
    public void ParseOpenApi_NonStringHeaderValue_ReturnsError()
    {
        using var server = new MiniHttpServer(MiniSpec);
        var tool = new OpenApiTool();

        var result = tool.ParseOpenApi(url: server.Url, headers: Json("{\"X-Int\":5}"));

        Assert.Contains("Header 'X-Int' must be a JSON string.", result);
    }

    [Fact]
    public void ParseOpenApi_UrlWithEmptyHeaderValue_SkipsHeader()
    {
        using var server = new MiniHttpServer(MiniSpec);
        var tool = new OpenApiTool();

        var result = tool.ParseOpenApi(url: server.Url, headers: Json("{\"X-Empty\":\"\"}"));

        Assert.DoesNotContain("Error", result);
        Assert.Contains("/ping", result);
        Assert.DoesNotContain("X-Empty", server.RequestHeaders ?? "");
    }

    [Fact]
    public void ParseOpenApi_UnableToSetHeader_ReturnsError()
    {
        using var server = new MiniHttpServer(MiniSpec);
        var tool = new OpenApiTool();

        var result = tool.ParseOpenApi(url: server.Url, headers: Json("{\"Bad Header\":\"x\"}"));

        Assert.Contains("Unable to set header 'Bad Header'.", result);
    }
}