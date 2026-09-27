using ApiMcp.Helpers;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace ApiMcp.AI.Tools;

[McpServerToolType]
public sealed class PostmanTool
{
    [McpServerTool, Description("""
Parses a Postman collection (v2.1) into its requests with resolved variables, collection/request auth, request bodies, and the embedded pm.test assertions (expected status codes and body-include checks).

- 'specification': the raw collection JSON text.
- 'path': a local file path to collection.json (read on the server machine).
- 'url': a URL to download the collection from. Provide exactly one of these.
- 'headers': optional JSON object for fetching a protected 'url'. Secret headers configured on the server are resolved automatically.
""")]
    public string ParsePostmanCollection(
        string? specification = null,
        string? path = null,
        string? url = null,
        [Description("JSON object of headers used only when fetching 'url', e.g. {\"Authorization\":\"Bearer ...\"}")]
        JsonElement? headers = null)
    {
        try
        {
            var json = LoadCollectionJson(specification, path, url, headers);
            var collection = PostmanParser.Parse(json);
            return PostmanParser.Render(collection);
        }
        catch (Exception ex)
        {
            return $"Error: unable to parse Postman collection: {ex.Message}";
        }
    }

    [McpServerTool, Description("""
Runs every request in a Postman collection (v2.1) against the live servers and evaluates the embedded pm.test assertions (expected status codes such as pm.response.to.have.status(200), and body-include checks such as pm.expect(pm.response.text()).to.include(...)). The common API-key/bearer/basic auth types are applied automatically and collection {{variables}} are resolved in the URL, headers and body.

Requests run in order. After each response, pm.collectionVariables/environment/variables/globals.set("name", <value>) calls are applied so later requests can use {{name}}; <value> may be a string literal, a pm.response.json() path (e.g. pm.response.json().data[0].id) or a let alias of one. Other JavaScript is not executed. A request with no recognized pm.test assertion is reported as [NOTEST] and is not counted as passed.

- 'specification': the raw collection JSON text. 'path': a local file path. 'url': a URL to download from. Provide exactly one.
- 'headers': optional JSON object for fetching a protected 'url'.
- 'variables': optional JSON object of variable overrides, e.g. {"base_url_bravo":"http://127.0.0.1:8080"}.
- 'timeoutSeconds': per-request timeout (default 30). 'followRedirects': follow redirects (default true).
Secret auth values are never printed; each request reports PASS/FAIL with per-assertion results.
""")]
    public string RunPostmanCollection(
        string? specification = null,
        string? path = null,
        string? url = null,
        [Description("JSON object of headers used only when fetching 'url'")]
        JsonElement? headers = null,
        [Description("JSON object of variable overrides, e.g. {\"base_url_bravo\":\"http://127.0.0.1:8080\"}")]
        JsonElement? variables = null,
        int timeoutSeconds = 30,
        bool followRedirects = true)
    {
        try
        {
            var json = LoadCollectionJson(specification, path, url, headers);
            var collection = PostmanParser.Parse(json);
            var overrides = ParseVariables(variables);
            return PostmanRunner.Run(collection, overrides, timeoutSeconds, followRedirects);
        }
        catch (Exception ex)
        {
            return $"Error: unable to run Postman collection: {ex.Message}";
        }
    }

    private static string LoadCollectionJson(string? specification, string? path, string? url, JsonElement? headers)
    {
        if (!string.IsNullOrWhiteSpace(specification))
            return specification;

        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!File.Exists(path))
                throw new InvalidOperationException($"Path '{path}' does not exist.");
            return File.ReadAllText(path);
        }

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Provide 'specification', 'path', or 'url'.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"'{url}' is not a valid absolute URL.");

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        if (headers is { ValueKind: JsonValueKind.Object })
        {
            foreach (var header in headers.Value.EnumerateObject())
            {
                if (header.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException($"Header '{header.Name}' must be a JSON string.");

                var value = HeaderStore.Resolve(header.Name, header.Value.GetString());
                if (string.IsNullOrEmpty(value))
                    continue;

                if (!request.Headers.TryAddWithoutValidation(header.Name, value))
                    throw new InvalidOperationException($"Unable to set header '{header.Name}'.");
            }
        }

        using var response = client.Send(request);
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Collection URL returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");

        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("Collection URL returned an empty document.");

        return body;
    }

    private static IReadOnlyDictionary<string, string>? ParseVariables(JsonElement? variables)
    {
        if (variables is not { ValueKind: JsonValueKind.Object } obj)
            return null;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
                map[prop.Name] = prop.Value.GetString() ?? "";
            else
                map[prop.Name] = prop.Value.GetRawText();
        }
        return map;
    }
}