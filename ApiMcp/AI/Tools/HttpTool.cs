using ApiMcp.Helpers;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace ApiMcp.AI.Tools;

[McpServerToolType]
public class HttpTool
{
    [McpServerTool, Description("""
 Sends an HTTP request (GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS, QUERY) to a URL and returns the status code, response headers, and response body. Use this as an API client for testing REST / HTTP endpoints.

 - `method`: HTTP method. QUERY is a safe, idempotent method for sending a query in the request body (like a read-only POST; RFC 9110 extension).
 - `url`: absolute URL, e.g. https://example.com/api/items
 - `headers`: optional JSON object, e.g. {"Content-Type": "application/json"}. Header values are literal strings, except that "${ENV_VAR}" is replaced by the value of the server's environment variable ENV_VAR (e.g. "Bearer ${TOKEN}"). A header that is configured as a secret on the server (--header NAME) is filled from the server's environment and the value you pass is ignored.
 - `query`: optional raw query string, e.g. "page=1&size=10", appended to the URL as-is.
 - `body`: optional raw request body. When omitted the request is sent without a body. Content-Type defaults to application/json for JSON-looking bodies.
 - `multipart`: optional JSON object to send a multipart/form-data request (single or multiple file upload). Shape:
   {"fields": {"name": "value"}, "files": [{"field": "file", "path": "C:\\a.png", "contentType": "image/png", "fileName": "a.png"}, {"field": "files", "path": "C:\\b.pdf"}]}
   Each file needs either `path` (local file on the server) or `contentBase64`. `field`, `fileName`, and `contentType` are optional. Add multiple entries with the same `field` to upload multiple files. When `multipart` is set, `body` is ignored and the multipart Content-Type (with boundary) overrides any you pass.
 - `timeoutSeconds`: request timeout in seconds (default 30).
 - `followRedirects`: follow HTTP redirects (default true).
 - `expectStatus`: optional expected HTTP status code. `expectBodyContains`: optional substring the response body must contain. When either is set, the result starts with a PASS/FAIL assertions block.
 - `select`: optional JSON path to return only part of a JSON response body, e.g. "$.items[*].{id,name}" or "$.accessToken". A path with a wildcard ([*], [], or *) returns an array.
 - `capture`: optional JSON object mapping names to JSON paths, e.g. {"token":"$.accessToken"}. The values are stored and can be reused in later requests as {{token}} anywhere in `url`, `query`, `headers`, `body`, or `multipart` fields. A missing variable is an error.
 """)]
    public string HttpRequest(
        string method,
        string url,
        [Description("JSON object of request headers, e.g. {\"Content-Type\":\"application/json\"}")]
        JsonElement? headers = null,
        [Description("Raw query string appended to the URL, e.g. page=1&size=10")]
        string? query = null,
        string? body = null,
        [Description("Multipart form: {\"fields\":{...},\"files\":[{...}]} for file uploads")]
        JsonElement? multipart = null,
        int timeoutSeconds = 30,
        bool followRedirects = true,
        [Description("Expected HTTP status code; when set, the result starts with a PASS/FAIL assertion block")]
        int? expectStatus = null,
        [Description("Substring the response body must contain; when set, the result starts with a PASS/FAIL assertion block")]
        string? expectBodyContains = null,
        [Description("JSON path to return only part of a JSON response body, e.g. $.items[*].{id,name}")]
        string? select = null,
        [Description("JSON object mapping variable names to JSON paths to capture from the response, e.g. {\"token\":\"$.accessToken\"}; reuse with {{token}} in later requests")]
        JsonElement? capture = null)
    {
        var headersJson = headers is { ValueKind: JsonValueKind.Object } h ? h.GetRawText() : null;
        var multipartJson = multipart is { ValueKind: JsonValueKind.Object } m ? m.GetRawText() : null;
        var captureJson = capture is { ValueKind: JsonValueKind.Object } c ? c.GetRawText() : null;
        return HttpHelper.Send(method, url, headersJson, query, body, multipartJson, timeoutSeconds, followRedirects, expectStatus, expectBodyContains, select, captureJson);
    }
}
