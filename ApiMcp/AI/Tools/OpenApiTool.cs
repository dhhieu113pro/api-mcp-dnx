using ApiMcp.Helpers;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace ApiMcp.AI.Tools;

[McpServerToolType]
public sealed class OpenApiTool
{
    [McpServerTool, Description("""
Parses an OpenAPI 3.x or Swagger 2.0 document and returns every HTTP endpoint with the exact method/path, parameters, request body content types, body schema, and a generated JSON example body.

Use this before http_request when testing an API from Swagger/OpenAPI. The returned requestBody.example is ready to send as the raw 'body' for JSON endpoints, and requestBody.contentType tells you which Content-Type header to use. Path/query/header parameters are listed separately. Supports JSON and YAML specifications.

- 'url': optional absolute URL to swagger.json, openapi.json, swagger.yaml, or openapi.yaml.
- 'specification': optional raw OpenAPI/Swagger JSON or YAML text. Provide either 'url' or 'specification'.
- 'headers': optional JSON object for fetching a protected specification. Secret headers configured on the server are resolved automatically.
""")]
    public string ParseOpenApi(
        string? url = null,
        string? specification = null,
        [Description("JSON object of headers used only when fetching 'url', e.g. {\"Authorization\":\"Bearer ...\"}")]
        JsonElement? headers = null)
    {
        try
        {
            var document = LoadSpecification(url, specification, headers);
            return OpenApiParser.Parse(document).Render();
        }
        catch (Exception ex)
        {
            return $"Error: unable to parse OpenAPI/Swagger document: {ex.Message}";
        }
    }

    private static string LoadSpecification(
        string? url,
        string? specification,
        JsonElement? headers)
    {
        if (!string.IsNullOrWhiteSpace(specification))
            return specification;

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Provide either 'url' or 'specification'.");

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
            throw new InvalidOperationException($"Swagger/OpenAPI URL returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");

        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("Swagger/OpenAPI URL returned an empty document.");

        return body;
    }
}
