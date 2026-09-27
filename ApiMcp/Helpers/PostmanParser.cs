using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApiMcp.Helpers;

internal static partial class PostmanParser
{
    [GeneratedRegex(@"\{\{([^}]+)\}\}")]
    private static partial Regex VariableToken();

    public static PostmanCollection Parse(string json, string? baseDirectory = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var info = Get(root, "info");
        var name = GetString(info, "name") ?? "Unnamed collection";
        var schema = GetString(info, "schema") ?? "";

        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Get(root, "variable") is { ValueKind: JsonValueKind.Array } variableArray)
        {
            foreach (var v in variableArray.EnumerateArray())
            {
                var key = GetString(v, "key");
                if (!string.IsNullOrWhiteSpace(key))
                    variables[key] = GetString(v, "value") ?? "";
            }
        }

        var items = new List<PostmanItem>();
        if (Get(root, "item") is { ValueKind: JsonValueKind.Array } itemArray)
            foreach (var node in itemArray.EnumerateArray())
                CollectItems(node, "", items, baseDirectory);

        return new PostmanCollection(name, schema, variables, ParseAuth(Get(root, "auth")), items);
    }

    public static string ResolveVariables(string value, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains("{{", StringComparison.Ordinal))
            return value;

        return VariableToken().Replace(value, match =>
            variables.TryGetValue(match.Groups[1].Value, out var resolved) ? resolved : match.Value);
    }

    public static string Render(PostmanCollection collection)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Postman collection: {collection.Name} ({collection.Items.Count} requests)");
        if (collection.Schema.Length > 0)
            sb.AppendLine($"Schema: {collection.Schema}");

        sb.AppendLine("Variables:");
        if (collection.Variables.Count == 0)
            sb.AppendLine("  (none)");
        else
            foreach (var kv in collection.Variables.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.AppendLine($"  {kv.Key} = {kv.Value}");

        sb.AppendLine($"Collection auth: {DescribeAuth(collection.Auth)}");

        sb.AppendLine("Requests:");
        int index = 0;
        foreach (var item in collection.Items)
        {
            index++;
            var url = ResolveVariables(item.Url, collection.Variables);
            var folder = item.Folder.Length == 0 ? "" : $" [{item.Folder}]";
            sb.AppendLine($"{index}. {item.Name}{folder}");
            sb.AppendLine($"    {item.Method} {url}");
            foreach (var header in item.Headers)
            {
                var value = ResolveVariables(header.Value, collection.Variables);
                sb.AppendLine($"    header {header.Name}: {value}");
            }
            var contentType = item.ContentType ?? GuessContentType(item.Body);
            if (contentType is { Length: > 0 })
                sb.AppendLine($"    content-type: {contentType}");
            if (item.Body is not null)
                sb.AppendLine($"    body: {ResolveVariables(item.Body, collection.Variables)}");
            if (item.Tests.Count == 0)
            {
                sb.AppendLine("    tests: (none)");
            }
            else
            {
                sb.AppendLine("    tests:");
                foreach (var test in item.Tests)
                {
                    var status = test.ExpectedStatus is int s ? $", status {s}" : "";
                    var includes = test.Includes.Count == 0 ? "" : $", includes [{string.Join("; ", test.Includes)}]";
                    sb.AppendLine($"      - \"{test.Name}\"{status}{includes}");
                }
            }
        }
        return sb.ToString();
    }

    private static string? GuessContentType(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        var t = body.TrimStart();
        if (t.StartsWith('{') || t.StartsWith('['))
            return "application/json";
        if (t.StartsWith('<'))
            return "application/xml";
        return "text/plain";
    }

    internal static string DescribeAuth(PostmanAuth? auth)
    {
        if (auth is null)
            return "none";
        var p = auth.Parameters;
        return auth.Type switch
        {
            PostmanConstants.AuthTypeApiKey => p.TryGetValue(PostmanConstants.AuthParamIn, out var location)
                ? $"api key in {location} ({Param(p, PostmanConstants.AuthParamKey)})"
                : $"api key ({Param(p, PostmanConstants.AuthParamKey)})",
            PostmanConstants.AuthTypeBearer => "bearer token",
            PostmanConstants.AuthTypeBasic => $"basic ({Param(p, PostmanConstants.AuthParamUsername)})",
            _ => auth.Type,
        };

        static string Param(Dictionary<string, string> p, string name) =>
            p.TryGetValue(name, out var v) && v.Length > 0 ? v : "<missing>";
    }

    private static void CollectItems(JsonElement node, string folder, List<PostmanItem> items, string? baseDirectory)
    {
        var name = GetString(node, "name") ?? "";
        if (Get(node, "request") is { ValueKind: JsonValueKind.Object } request)
        {
            items.Add(ParseItem(node, name, folder, request, baseDirectory));
            return;
        }

        var childFolder = name.Length == 0 ? folder : (folder.Length == 0 ? name : $"{folder} / {name}");
        if (Get(node, "item") is { ValueKind: JsonValueKind.Array } children)
            foreach (var child in children.EnumerateArray())
                CollectItems(child, childFolder, items, baseDirectory);
    }

    private static PostmanItem ParseItem(JsonElement node, string name, string folder, JsonElement request, string? baseDirectory)
    {
        var rawMethod = GetString(request, "method");
        var method = string.IsNullOrWhiteSpace(rawMethod) ? "GET" : rawMethod.Trim().ToUpperInvariant();
        var url = GetUrl(request);

        var headers = new List<PostmanHeader>();
        if (Get(request, "header") is { ValueKind: JsonValueKind.Array } headerArray)
        {
            foreach (var h in headerArray.EnumerateArray())
            {
                var headerName = GetString(h, "key");
                if (!string.IsNullOrWhiteSpace(headerName))
                    headers.Add(new PostmanHeader(headerName, GetString(h, "value") ?? ""));
            }
        }

        var auth = ParseAuth(Get(request, "auth"));
        var (body, contentType) = ParseBody(request);
        var multipart = ParseMultipart(request, baseDirectory);
        var script = CollectTestScript(node);
        var tests = ExtractTests(script);

        return new PostmanItem(folder, name, method, url, headers, auth, body, contentType, tests, script, multipart);
    }

    private static string GetUrl(JsonElement request)
    {
        if (Get(request, "url") is not { ValueKind: JsonValueKind.Object } url)
            return "";

        var raw = GetString(url, "raw");
        if (!string.IsNullOrWhiteSpace(raw))
            return raw;

        var sb = new StringBuilder();
        var protocol = GetString(url, "protocol");
        if (!string.IsNullOrWhiteSpace(protocol))
            sb.Append(protocol).Append("://");

        var host = Get(url, "host");
        if (host is { } h)
        {
            if (h.ValueKind == JsonValueKind.String)
                sb.Append(h.GetString());
            else if (h.ValueKind == JsonValueKind.Array)
                sb.Append(string.Join(".", h.EnumerateArray().Select(x => x.GetString() ?? "")));
        }

        var port = GetString(url, "port");
        if (!string.IsNullOrWhiteSpace(port))
            sb.Append(':').Append(port);

        if (Get(url, "path") is { ValueKind: JsonValueKind.Array } path)
        {
            var segments = string.Join("/", path.EnumerateArray().Select(x => x.GetString() ?? ""));
            if (segments.Length > 0)
                sb.Append('/').Append(segments);
        }

        if (Get(url, "query") is { ValueKind: JsonValueKind.Array } query)
        {
            var pairs = new List<string>();
            foreach (var q in query.EnumerateArray())
            {
                var qk = GetString(q, "key");
                if (string.IsNullOrEmpty(qk))
                    continue;
                var qv = GetString(q, "value") ?? "";
                pairs.Add($"{qk}={Uri.EscapeDataString(qv)}");
            }
            if (pairs.Count > 0)
                sb.Append('?').Append(string.Join("&", pairs));
        }

        return sb.ToString();
    }

    private static PostmanAuth? ParseAuth(JsonElement? authEl)
    {
        if (authEl is not { } auth || auth.ValueKind != JsonValueKind.Object)
            return null;

        var type = GetString(auth, "type");
        if (string.IsNullOrWhiteSpace(type))
            return null;

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Get(auth, type) is { ValueKind: JsonValueKind.Array } entries)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                var key = GetString(entry, "key");
                if (!string.IsNullOrWhiteSpace(key))
                    parameters[key] = GetString(entry, "value") ?? "";
            }
        }

        return new PostmanAuth(type, parameters);
    }

    private static (string? Body, string? ContentType) ParseBody(JsonElement request)
    {
        if (Get(request, "body") is not { ValueKind: JsonValueKind.Object } body)
            return (null, null);

        var mode = GetString(body, "mode");
        return mode switch
        {
            "raw" => (GetString(body, "raw"), null),
            "urlencoded" => (JoinKeyValues(Get(body, "urlencoded")), "application/x-www-form-urlencoded"),
            "formdata" => (JoinKeyValues(Get(body, "formdata")), "application/x-www-form-urlencoded"),
            "graphql" => (BuildGraphQl(body), "application/json"),
            _ => (null, null),
        };
    }

    // formdata with at least one file entry becomes a multipart request; relative src paths resolve
    // against baseDirectory (the collection file folder), like Newman's --working-dir.
    private static string? ParseMultipart(JsonElement request, string? baseDirectory)
    {
        if (Get(request, "body") is not { ValueKind: JsonValueKind.Object } body
            || GetString(body, "mode") != "formdata"
            || Get(body, "formdata") is not { ValueKind: JsonValueKind.Array } entries)
            return null;

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new List<Dictionary<string, string>>();
        foreach (var entry in entries.EnumerateArray())
        {
            var key = GetString(entry, "key");
            if (string.IsNullOrEmpty(key) || GetBool(entry, "disabled"))
                continue;
            if (GetString(entry, "type") != "file")
            {
                fields[key] = GetString(entry, "value") ?? "";
                continue;
            }

            var sources = Get(entry, "src") switch
            {
                { ValueKind: JsonValueKind.String } s => [s.GetString() ?? ""],
                { ValueKind: JsonValueKind.Array } a => a.EnumerateArray().Select(x => x.GetString() ?? "").ToList(),
                _ => new List<string>(),
            };
            foreach (var src in sources.Where(s => s.Length > 0))
            {
                var path = Path.IsPathRooted(src) || baseDirectory is null ? src : Path.Combine(baseDirectory, src);
                var file = new Dictionary<string, string> { ["field"] = key, ["path"] = path };
                if (GetString(entry, "contentType") is { Length: > 0 } fileContentType)
                    file["contentType"] = fileContentType;
                files.Add(file);
            }
        }

        return files.Count == 0 ? null : JsonSerializer.Serialize(new { fields, files });
    }

    private static bool GetBool(JsonElement element, string name) =>
        Get(element, name) is { ValueKind: JsonValueKind.True };

    private static string? JoinKeyValues(JsonElement? entries)
    {
        if (entries is not { } list || list.ValueKind != JsonValueKind.Array)
            return null;
        var pairs = new List<string>();
        foreach (var entry in list.EnumerateArray())
        {
            var key = GetString(entry, "key");
            if (string.IsNullOrEmpty(key))
                continue;
            var value = GetString(entry, "value") ?? "";
            pairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }
        return pairs.Count == 0 ? null : string.Join("&", pairs);
    }

    private static string? BuildGraphQl(JsonElement body)
    {
        if (Get(body, "graphql") is not { ValueKind: JsonValueKind.Object } graphQl)
            return null;
        var query = GetString(graphQl, "query") ?? "";
        var variables = Get(graphQl, "variables");
        var variablesText = variables is { } v && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        return JsonSerializer.Serialize(
            new Dictionary<string, string?>
            {
                ["query"] = query,
                ["variables"] = variablesText,
            },
            new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
    }

    private static string CollectTestScript(JsonElement node)
    {
        if (Get(node, "event") is not { ValueKind: JsonValueKind.Array } events)
            return "";
        var sb = new StringBuilder();
        foreach (var ev in events.EnumerateArray())
        {
            if (GetString(ev, "listen") != "test")
                continue;
            if (Get(ev, "script") is not { ValueKind: JsonValueKind.Object } script)
                continue;
            if (Get(script, "exec") is not { ValueKind: JsonValueKind.Array } exec)
                continue;
            foreach (var line in exec.EnumerateArray())
                if (line.ValueKind == JsonValueKind.String)
                    sb.AppendLine(line.GetString() ?? "");
        }
        return sb.ToString();
    }

    // Extracts the common pm.test assertions: names, expected status and
    // body-include expressions. Full JavaScript execution is not attempted.
    internal static List<PostmanTest> ExtractTests(string script)
    {
        var tests = new List<PostmanTest>();
        if (string.IsNullOrEmpty(script))
            return tests;

        foreach (var block in Regex.Split(script, @"(?=pm\.test\()"))
        {
            var name = Regex.Match(block, @"pm\.test\(\s*([""'`])((?:(?!\1).)*)\1").Groups[2].Value;
            if (name.Length == 0)
                continue;
            int? expectedStatus = null;
            var status = Regex.Match(block, @"\.have\.status\(\s*(\d+)\s*\)");
            if (status.Success && int.TryParse(status.Groups[1].Value, out var parsed))
                expectedStatus = parsed;
            var includes = new List<string>();
            foreach (Match include in Regex.Matches(block, @"to\.include\(\s*([^)]*?)\s*\)"))
            {
                var expr = include.Groups[1].Value.Trim();
                if (expr.Length > 0)
                    includes.Add(expr);
            }
            tests.Add(new PostmanTest(name, expectedStatus, includes));
        }
        return tests;
    }

    private static JsonElement? Get(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value : null;

    private static string? GetString(JsonElement? element, string propertyName) =>
        element is { } obj && Get(obj, propertyName) is { ValueKind: JsonValueKind.String } text
            ? text.GetString()
            : null;
}