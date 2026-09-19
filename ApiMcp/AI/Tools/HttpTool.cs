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
 - `headers`: optional JSON object, e.g. {"Content-Type": "application/json"}. Header values are literal strings. A header that is configured as a secret on the server is always filled from the server's environment and the value you pass is ignored (see list_secret_headers).
 - `query`: optional raw query string, e.g. "page=1&size=10", appended to the URL as-is. A parameter configured as a secret on the server is always filled from the server's environment and the value you pass is ignored (see list_secret_query_params) — for APIs that take the key on the query string.
 - `body`: optional raw request body. When omitted the request is sent without a body. Content-Type defaults to application/json for JSON-looking bodies.
 - `multipart`: optional JSON object to send a multipart/form-data request (single or multiple file upload). Shape:
   {"fields": {"name": "value"}, "files": [{"field": "file", "path": "C:\\a.png", "contentType": "image/png", "fileName": "a.png"}, {"field": "files", "path": "C:\\b.pdf"}]}
   Each file needs either `path` (local file on the server) or `contentBase64`. `field`, `fileName`, and `contentType` are optional. Add multiple entries with the same `field` to upload multiple files. When `multipart` is set, `body` is ignored and the multipart Content-Type (with boundary) overrides any you pass.
 - `timeoutSeconds`: request timeout in seconds (default 30).
 - `followRedirects`: follow HTTP redirects (default true).
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
        bool followRedirects = true)
    {
        var headersJson = headers is { ValueKind: JsonValueKind.Object } h ? h.GetRawText() : null;
        var multipartJson = multipart is { ValueKind: JsonValueKind.Object } m ? m.GetRawText() : null;
        return HttpHelper.Send(method, url, headersJson, query, body, multipartJson, timeoutSeconds, followRedirects);
    }

    [McpServerTool, Description("""
 Lists the request header names that are owned by the server and resolved from environment variables. Their values are never exposed to the caller. Use these header names in the 'headers' parameter of http_request and the server will fill the secret automatically.
 """)]
    public string ListSecretHeaders()
    {
        var names = HeaderStore.SecretNames();
        if (names.Count == 0)
            return "No secret headers configured. Start the server with --header-env Name=ENV_VAR (or set APIMCP_HEADER_ENV) and restart.";
        return "Secret headers (values are server-side, never shown):\n" + string.Join("\n", names.Select(n => $" - {n}"));
    }

    [McpServerTool, Description("""
 Lists the query-string parameter names that are owned by the server and resolved from environment variables. Their values are never exposed to the caller. Use these parameter names in the 'query' argument of http_request (the value you pass is ignored) and the server fills the secret automatically. Typical for APIs that take an API key on the query string, e.g. 'api_key=...'.
 """)]
    public string ListSecretQueryParams()
    {
        var names = QueryParamStore.SecretNames();
        if (names.Count == 0)
            return "No secret query parameters configured. Start the server with --query-env Name=ENV_VAR (or set APIMCP_QUERY_ENV) and restart.";
        return "Secret query parameters (values are server-side, never shown):\n" + string.Join("\n", names.Select(n => $" - {n}"));
    }
}