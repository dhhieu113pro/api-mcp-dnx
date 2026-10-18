using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApiMcp.Helpers;

// Minimal JSON path selector used by http_request's 'select' and 'capture':
//   $.a.b        property navigation (a leading "$." or "$" is optional)
//   [0]          array index
//   [*] or []    all array elements
//   *            all properties of an object
//   {x,y}        project these properties from each object
// A path containing a wildcard ([*], [], or *) returns a JSON array; otherwise the single matched value.
internal static class JsonSelector
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    /// <summary>Returns the selected nodes as pretty-printed JSON.</summary>
    public static string Select(string content, string path)
    {
        var nodes = Walk(content, path, out var wildcard);
        if (!wildcard && nodes.Count == 1)
            return nodes[0]?.ToJsonString(Indented) ?? "null";
        return BuildArray(nodes).ToJsonString(Indented);
    }

    /// <summary>Returns a single captured value as plain text (JSON strings are unquoted).</summary>
    public static string SelectScalar(string content, string path)
    {
        var nodes = Walk(content, path, out var wildcard);
        if (!wildcard && nodes.Count == 0)
            throw new InvalidOperationException($"No value matched path '{path}'.");
        var node = !wildcard && nodes.Count == 1 ? nodes[0] : BuildArray(nodes);
        return Scalar(node);
    }

    private static List<JsonNode?> Walk(string content, string path, out bool wildcard)
    {
        var nodes = new List<JsonNode?> { ParseRoot(content) };
        var hasWildcard = false;
        foreach (var segment in Tokenize(path))
            nodes = Apply(nodes, segment, ref hasWildcard, path);
        wildcard = hasWildcard;
        return nodes;
    }

    private static JsonNode ParseRoot(string content)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("'select'/'capture' requires a JSON response body.");
        }

        return root ?? throw new InvalidOperationException("'select'/'capture' requires a JSON response body.");
    }

    private static JsonArray BuildArray(List<JsonNode?> nodes)
    {
        var array = new JsonArray();
        foreach (var node in nodes)
            array.Add(node?.DeepClone());
        return array;
    }

    private static string Scalar(JsonNode? node) => node switch
    {
        null => "",
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node!.ToJsonString(Compact),
    };

    private static List<string> Tokenize(string path)
    {
        var text = path.Trim();
        if (text.StartsWith('$'))
            text = text[1..];

        var segments = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '.')
            {
                i++;
                continue;
            }
            if (c == '[')
            {
                var close = text.IndexOf(']', i);
                if (close < 0)
                    throw new InvalidOperationException($"Invalid 'select' path '{path}': missing ']'.");
                segments.Add(text[i..(close + 1)]);
                i = close + 1;
                continue;
            }
            if (c == '{')
            {
                var close = text.IndexOf('}', i);
                if (close < 0)
                    throw new InvalidOperationException($"Invalid 'select' path '{path}': missing '}}'.");
                segments.Add(text[i..(close + 1)]);
                i = close + 1;
                continue;
            }

            var start = i;
            while (i < text.Length && text[i] != '.' && text[i] != '[' && text[i] != '{')
                i++;
            segments.Add(text[start..i]);
        }
        return segments;
    }

    private static List<JsonNode?> Apply(List<JsonNode?> nodes, string segment, ref bool wildcard, string path)
    {
        var result = new List<JsonNode?>();

        if (segment.StartsWith('['))
        {
            var inner = segment[1..^1].Trim();
            foreach (var node in nodes)
            {
                if (node is not JsonArray array)
                    continue;
                if (inner is "*" or "")
                {
                    wildcard = true;
                    foreach (var item in array)
                        result.Add(item);
                }
                else if (int.TryParse(inner, out var index))
                {
                    if (index >= 0 && index < array.Count)
                        result.Add(array[index]);
                }
                else
                {
                    throw new InvalidOperationException($"Invalid 'select' index '[{inner}]' in '{path}'.");
                }
            }
            return result;
        }

        if (segment.StartsWith('{'))
        {
            var fields = segment[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var node in nodes)
            {
                if (node is not JsonObject obj)
                    continue;
                var projected = new JsonObject();
                foreach (var field in fields)
                    if (obj.TryGetPropertyValue(field, out var value))
                        projected[field] = value?.DeepClone();
                result.Add(projected);
            }
            return result;
        }

        if (segment == "*")
        {
            wildcard = true;
            foreach (var node in nodes)
                if (node is JsonObject obj)
                    foreach (var pair in obj)
                        result.Add(pair.Value);
            return result;
        }

        foreach (var node in nodes)
            if (node is JsonObject obj && obj.TryGetPropertyValue(segment, out var value))
                result.Add(value);

        return result;
    }
}
