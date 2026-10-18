using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public class VariableCaptureTests
{
    [Fact]
    public void Interpolate_ReplacesKnownVariables()
    {
        VariableStore.Set("vt_token", "abc");
        VariableStore.Set("vt_id", "7");

        Assert.Equal("Bearer abc/7", VariableStore.Interpolate("Bearer {{vt_token}}/{{vt_id}}"));
    }

    [Fact]
    public void Interpolate_NoBraces_ReturnsInput()
    {
        Assert.Equal("plain text", VariableStore.Interpolate("plain text"));
    }

    [Fact]
    public void Interpolate_UnknownVariable_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => VariableStore.Interpolate("{{vt_missing}}"));
        Assert.Contains("is not set", ex.Message);
    }

    [Fact]
    public void Capture_StoresValues_AndTemplatingReusesThem()
    {
        using var server = new MiniHttpServer("{\"accessToken\":\"tok-123\",\"userId\":42}", responseContentType: "application/json");

        var first = HttpHelper.Send("GET", server.Url, null, null, null, null, 10, true,
            capture: "{\"vt_token\":\"$.accessToken\",\"vt_uid\":\"$.userId\"}");

        Assert.Contains("Captured:", first);
        Assert.Contains("vt_token = tok-123", first);
        Assert.Contains("vt_uid = 42", first);

        var second = HttpHelper.Send("GET", server.Url + "items/{{vt_uid}}", "{\"X-Token\":\"Bearer {{vt_token}}\"}", null, null, null, 10, true);

        Assert.Contains("items/42", server.RequestLine ?? "");
        Assert.Contains("Bearer tok-123", server.RequestHeaders ?? "");
    }

    [Fact]
    public void Templating_AppliesToBody()
    {
        using var server = new MiniHttpServer("{\"id\":\"abc\"}", responseContentType: "application/json");

        HttpHelper.Send("GET", server.Url, null, null, null, null, 10, true, capture: "{\"vt_body_id\":\"$.id\"}");
        HttpHelper.Send("POST", server.Url, null, null, "{\"ref\":\"{{vt_body_id}}\"}", null, 10, true);

        Assert.Contains("\"ref\":\"abc\"", server.RequestBodyText ?? "");
    }

    [Fact]
    public void Templating_UnknownVariable_ReturnsError()
    {
        using var server = new MiniHttpServer();

        var result = HttpHelper.Send("GET", server.Url + "{{vt_nope}}", null, null, null, null, 10, true);

        Assert.Contains("Variable 'vt_nope' is not set", result);
    }

    [Fact]
    public void Capture_NonJsonBody_ReportsFailureButReturnsResponse()
    {
        using var server = new MiniHttpServer("not json");

        var result = HttpHelper.Send("GET", server.Url, null, null, null, null, 10, true, capture: "{\"vt_bad\":\"$.a\"}");

        Assert.Contains("Captured:", result);
        Assert.Contains("vt_bad = <failed:", result);
        Assert.Contains("HTTP 200", result);
    }

    [Fact]
    public void Capture_MissingPath_ReportsFailure()
    {
        using var server = new MiniHttpServer("{\"a\":1}", responseContentType: "application/json");

        var result = HttpHelper.Send("GET", server.Url, null, null, null, null, 10, true, capture: "{\"vt_x\":\"$.nope\"}");

        Assert.Contains("vt_x = <failed: No value matched path '$.nope'.>", result);
    }
}
