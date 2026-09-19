using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Interfaces;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Readers.Exceptions;
using System.Text.Json;

namespace ApiMcp.Helpers;

internal sealed class OpenApiParser
{
    private readonly OpenApiDocument _document;

    private OpenApiParser(OpenApiDocument document) => _document = document;

    internal static OpenApiParser Parse(string specification)
    {
        if (string.IsNullOrWhiteSpace(specification))
            throw new InvalidOperationException("'specification' is required.");

        var reader = new OpenApiStringReader();
        OpenApiDocument document;
        OpenApiDiagnostic diagnostic;
        try
        {
            document = reader.Read(specification, out diagnostic);
        }
        catch (OpenApiUnsupportedSpecVersionException ex)
        {
            throw new InvalidOperationException($"Unable to parse the OpenAPI/Swagger document: {ex.Message}", ex);
        }

        if (document is null)
            throw new InvalidOperationException("Unable to parse the OpenAPI/Swagger document.");

        if (diagnostic.Errors.Count > 0)
        {
            var errors = diagnostic.Errors
                .Where(e => !string.Equals(
                    e.Message,
                    "Responses must contain at least one response",
                    StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .Select(e => e.Message);
            var message = string.Join("; ", errors);
            if (!string.IsNullOrWhiteSpace(message))
                throw new InvalidOperationException($"OpenAPI/Swagger parse errors: {message}");
        }

        return new OpenApiParser(document);
    }

    internal string Render()
    {
        var root = new Dictionary<string, object?>
        {
            ["title"] = _document.Info?.Title,
            ["version"] = _document.Info?.Version,
            ["baseUrls"] = GetBaseUrls(),
            ["endpoints"] = (_document.Paths ?? [])
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .SelectMany(p => p.Value.Operations
                    .OrderBy(o => o.Key.ToString(), StringComparer.Ordinal)
                    .Select(o => DescribeOperation(p.Key, o.Key, o.Value)))
                .ToArray()
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }

    private string[] GetBaseUrls()
    {
        return _document.Servers is { Count: > 0 }
            ? _document.Servers.Select(s => s.Url).ToArray()
            : [];
    }

    private object DescribeOperation(string path, OperationType method, OpenApiOperation operation)
    {
        // Parameters can be defined at both Path Item and Operation level.
        // Swagger 2.0 body/formData parameters are normalized by OpenAPI.NET
        // into Operation.RequestBody when reading a Swagger 2.0 document.
        var allParameters = GetParameters(path, operation);

        var parameters = allParameters
            .Select(p => new
            {
                name = p.Name,
                location = p.In?.ToString().ToLowerInvariant(),
                required = p.Required,
                description = p.Description,
                schema = SchemaDescription(p.Schema),
                example = ExampleForSchema(p.Schema, p.Example)
            })
            .ToArray();

        var requestBodies = operation.RequestBody?.Content?
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                contentType = x.Key,
                required = operation.RequestBody.Required,
                schema = SchemaDescription(x.Value.Schema),
                example = ExampleForSchema(x.Value.Schema, x.Value.Example)
            })
            .ToList() ?? [];

        return new
        {
            method = method.ToString().ToUpperInvariant(),
            path,
            operationId = operation.OperationId,
            summary = operation.Summary,
            description = operation.Description,
            parameters,
            requestBody = requestBodies.FirstOrDefault(),
            requestBodies,
            responses = (operation.Responses ?? [])
                .Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            security = operation.Security?.Count > 0
                ? operation.Security
                    .SelectMany(x => x.Keys)
                    .Select(x => x.Reference?.Id ?? x.Scheme ?? x.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
                : null
        };
    }

    private IReadOnlyList<OpenApiParameter> GetParameters(string path, OpenApiOperation operation)
    {
        var pathParameters = _document.Paths?[path].Parameters ?? [];
        return pathParameters
            .Concat(operation.Parameters ?? [])
            .GroupBy(p => $"{p.In}:{p.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToArray();
    }

    private object? SchemaDescription(OpenApiSchema? schema)
    {
        if (schema is null)
            return null;

        schema = Resolve(schema);
        return new
        {
            type = schema.Type,
            format = schema.Format,
            nullable = schema.Nullable,
            required = schema.Required?.ToArray(),
            properties = schema.Properties?.ToDictionary(
                p => p.Key,
                p => SchemaDescription(p.Value),
                StringComparer.Ordinal),
            items = schema.Items is null ? null : SchemaDescription(schema.Items),
            enumValues = schema.Enum?.Select(ValueToString).ToArray(),
            defaultValue = schema.Default is null ? null : ValueToString(schema.Default)
        };
    }

    private object? ExampleForSchema(OpenApiSchema? schema, IOpenApiAny? explicitExample)
    {
        if (explicitExample is not null)
            return ToJsonValue(explicitExample);

        if (schema is null)
            return null;

        schema = Resolve(schema);

        if (schema.Example is not null)
            return ToJsonValue(schema.Example);

        if (schema.Default is not null)
            return ToJsonValue(schema.Default);

        if (schema.Enum is { Count: > 0 })
            return ToJsonValue(schema.Enum[0]);

        if (schema.Type?.Equals("object", StringComparison.OrdinalIgnoreCase) == true ||
            schema.Properties?.Count > 0)
        {
            var result = new Dictionary<string, object?>();
            foreach (var property in schema.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
                result[property.Key] = ExampleForSchema(property.Value, null);
            return result;
        }

        if (schema.Type?.Equals("array", StringComparison.OrdinalIgnoreCase) == true)
            return new[] { ExampleForSchema(schema.Items, null) };

        return schema.Type?.ToLowerInvariant() switch
        {
            "integer" => 0,
            "number" => 0,
            "boolean" => false,
            "string" when schema.Format?.Equals("uuid", StringComparison.OrdinalIgnoreCase) == true
                => "00000000-0000-0000-0000-000000000000",
            "string" when schema.Format?.Equals("date", StringComparison.OrdinalIgnoreCase) == true
                => "2026-01-01",
            "string" when schema.Format?.Equals("date-time", StringComparison.OrdinalIgnoreCase) == true
                => "2026-01-01T00:00:00Z",
            "string" => "string",
            _ => null
        };
    }

    private OpenApiSchema Resolve(OpenApiSchema schema)
    {
        var current = schema;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (current.Reference is { Id: not null } reference &&
               visited.Add(reference.Id) &&
               _document.Components?.Schemas?.TryGetValue(reference.Id, out var resolved) == true)
        {
            current = resolved;
        }

        return current;
    }

    private static object? ToJsonValue(IOpenApiAny value)
    {
        return value switch
        {
            OpenApiString s => s.Value,
            OpenApiInteger i => i.Value,
            OpenApiLong l => l.Value,
            OpenApiFloat f => f.Value,
            OpenApiDouble d => d.Value,
            OpenApiBoolean b => b.Value,
            OpenApiNull => null,
            OpenApiArray a => a.Select(ToJsonValue).ToArray(),
            OpenApiObject o => o.ToDictionary(x => x.Key, x => ToJsonValue(x.Value)),
            _ => value.ToString()
        };
    }

    private static string? ValueToString(IOpenApiAny value) => ToJsonValue(value)?.ToString();
}
