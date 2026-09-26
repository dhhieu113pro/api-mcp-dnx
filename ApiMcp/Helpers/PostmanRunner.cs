using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApiMcp.Helpers;

internal static partial class PostmanRunner
{
    private static readonly Regex LetDefinition =
        LetDefinitionRegex();

    [GeneratedRegex(@"let\s+([A-Za-z_]\w*)\s*=\s*([^;]+);\s*", RegexOptions.Compiled)]
    private static partial Regex LetDefinitionRegex();

    public static string Run(
        PostmanCollection collection,
        IReadOnlyDictionary<string, string>? variableOverrides,
        int timeoutSeconds,
        bool followRedirects)
    {
        var variables = new Dictionary<string, string>(collection.Variables, StringComparer.Ordinal);
        if (variableOverrides is not null)
            foreach (var kv in variableOverrides)
                variables[kv.Key] = kv.Value;

        var total = StopwatchStart();
        var blocks = new List<string>();
        int passed = 0;
        foreach (var item in collection.Items)
        {
            var block = RunItem(item, collection, variables, timeoutSeconds, followRedirects);
            if (block.StartsWith("[PASS]", StringComparison.Ordinal))
                passed++;
            blocks.Add(block);
        }
        var elapsed = StopwatchStop(total);
        var failed = collection.Items.Count - passed;

        var summary = new StringBuilder();
        summary.AppendLine(
            $"Postman collection: {collection.Name} ({collection.Items.Count} requests) — " +
            $"{passed} passed, {failed} failed, {elapsed:0.###}s");
        foreach (var block in blocks)
            summary.AppendLine(block);
        return summary.ToString();
    }

    private static string RunItem(
        PostmanItem item,
        PostmanCollection collection,
        IReadOnlyDictionary<string, string> variables,
        int timeoutSeconds,
        bool followRedirects)
    {
        var sb = new StringBuilder();
        var url = PostmanParser.ResolveVariables(item.Url, variables);
        var effectiveAuth = item.Auth ?? collection.Auth;
        var headers = BuildEffectiveHeaders(item, effectiveAuth, variables);
        var authDescription = AuthFor(item, effectiveAuth, variables);
        var headersJson = BuildHeadersJson(headers, item.ContentType);

        HttpExchange exchange;
        try
        {
            exchange = HttpHelper.Execute(
                item.Method, url, headersJson, null, item.Body,
                multipartJson: null, timeoutSeconds, followRedirects);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[ERROR] {item.Name}");
            sb.AppendLine($"  {item.Method} {url}");
            sb.AppendLine($"  Failed: {ex.Message}");
            return sb.ToString();
        }

        var passed = EvaluateTests(
            item, exchange,
            out var testLines);

        var label = passed ? "PASS" : "FAIL";
        sb.AppendLine($"[{label}] {item.Name} ({exchange.ElapsedMs}ms)");
        sb.AppendLine($"  {item.Method} {url}");
        if (authDescription is not null)
            sb.AppendLine($"  {authDescription}");
        sb.AppendLine($"  HTTP {exchange.StatusCode} {exchange.ReasonPhrase}");
        foreach (var line in testLines)
            sb.AppendLine($"  {line}");
        return sb.ToString();
    }

    private static bool EvaluateTests(
        PostmanItem item,
        HttpExchange exchange,
        out List<string> testLines)
    {
        var lets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in LetDefinition.Matches(item.Script))
        {
            var name = m.Groups[1].Value;
            var definition = m.Groups[2].Value.Trim();
            if (definition.Length > 0)
                lets[name] = definition;
        }

        testLines = new List<string>();
        bool allPassed = true;
        foreach (var test in item.Tests)
        {
            var testPassed = true;
            var parts = new List<string>();
            if (test.ExpectedStatus is int expected)
            {
                var statusOk = exchange.StatusCode == expected;
                testPassed &= statusOk;
                parts.Add(statusOk
                    ? $"status {expected} OK"
                    : $"expected status {expected}, got {exchange.StatusCode}");
            }
            foreach (var include in test.Includes)
            {
                var (ok, includeDetail) = EvaluateInclude(exchange, include, lets);
                testPassed &= ok;
                parts.Add(includeDetail);
            }

            var mark = testPassed ? "✓" : "✗";
            var detail = parts.Count == 0 ? "no assertions" : string.Join("; ", parts);
            testLines.Add($"{mark} \"{test.Name}\" — {detail}");
            allPassed &= testPassed;
        }
        if (item.Tests.Count == 0)
            testLines.Add("• no test assertions in collection item");
        return allPassed;
    }

    private static (bool Ok, string Detail) EvaluateInclude(
        HttpExchange exchange,
        string expression,
        IReadOnlyDictionary<string, string> lets)
    {
        var value = ResolveIncludeValue(exchange.Body, expression, lets);
        if (value is null)
            return (false, $"could not evaluate body include of '{expression}'");

        var found = exchange.Body.Contains(value, StringComparison.Ordinal);
        var quoted = value.Length <= 60 ? $"\"{value}\"" : $"\"{value[..57]}…\"";
        return found
            ? (true, $"body contains {quoted}")
            : (false, $"body does not contain {quoted}");
    }

    private static string? ResolveIncludeValue(string body, string expression, IReadOnlyDictionary<string, string> lets)
    {
        var expr = expression.Trim();
        if (expr.Length == 0)
            return null;

        var literal = Unquote(expr);
        if (literal is not null)
            return literal;

        var path = ResolveIncludePath(expr, lets);
        if (path is null)
            return null;

        var element = WalkJsonPath(body, path);
        return element is { } e && e.ValueKind is JsonValueKind.Null
            ? "null"
            : stringify(element);
    }

    private static string? ResolveIncludePath(string expr, IReadOnlyDictionary<string, string> lets)
    {
        const string responseJsonPrefix = "pm.response.json()";

        string candidate;
        bool rootIsResponse;
        if (expr.StartsWith(responseJsonPrefix, StringComparison.Ordinal))
        {
            candidate = expr[responseJsonPrefix.Length..];
            rootIsResponse = true;
        }
        else if (lets.TryGetValue(expr, out var definition))
        {
            candidate = definition;
            rootIsResponse = candidate.StartsWith(responseJsonPrefix, StringComparison.Ordinal);
            if (rootIsResponse)
                candidate = candidate[responseJsonPrefix.Length..];
        }
        else
        {
            candidate = expr;
            rootIsResponse = false;
        }

        if (candidate.Length == 0)
            return null;
        if (rootIsResponse)
            return candidate;

        var stripped = StripRootIdentifier(candidate);
        return stripped is { Length: > 0 } ? stripped : candidate;
    }

    private static string? Unquote(string expr)
    {
        if (expr.Length < 2)
            return null;
        var quote = expr[0];
        if (quote is not '"' and not '\'')
            return null;
        if (expr[^1] != quote)
            return null;
        return expr[1..^1];
    }

    private static string? StripRootIdentifier(string expr)
    {
        var match = Regex.Match(expr, @"^([A-Za-z_]\w*)");
        if (!match.Success)
            return null;
        var tail = expr[match.Length..];
        return tail.StartsWith('.') ? tail[1..] : (tail.Length == 0 ? null : tail);
    }

    private static JsonElement? WalkJsonPath(string body, string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        JsonElement? current;
        try
        {
            using var doc = JsonDocument.Parse(body);
            current = doc.RootElement;
            if (current.Value.ValueKind != JsonValueKind.Object &&
                current.Value.ValueKind != JsonValueKind.Array)
                return null;
        }
        catch (JsonException)
        {
            return null;
        }

        int index = 0;
        while (index < path.Length)
        {
            JsonElement? stepped = null;
            if (path[index] == '.')
            {
                index++;
                stepped = StepIdent(path, ref index, current.Value);
            }
            else if (path[index] == '[')
            {
                stepped = StepIndex(path, ref index, current.Value);
            }
            else
            {
                stepped = StepIdent(path, ref index, current.Value);
            }

            if (stepped is not { } element)
                return null;
            current = element;
        }
        return current;
    }

    private static JsonElement? StepIdent(string path, ref int index, JsonElement current)
    {
        var start = index;
        while (index < path.Length && (char.IsLetterOrDigit(path[index]) || path[index] == '_'))
            index++;
        if (index == start)
            return null;
        var name = path[start..index];
        return current.ValueKind == JsonValueKind.Object &&
               current.TryGetProperty(name, out var value)
            ? value
            : null;
    }

    private static JsonElement? StepIndex(string path, ref int index, JsonElement current)
    {
        index++;
        var start = index;
        while (index < path.Length && char.IsDigit(path[index]))
            index++;
        if (index == start || index >= path.Length || path[index] != ']')
            return null;
        if (!int.TryParse(path[start..index], out var arrayIndex))
            return null;
        index++;
        if (current.ValueKind != JsonValueKind.Array || arrayIndex < 0 || arrayIndex >= current.GetArrayLength())
            return null;
        return current[arrayIndex];
    }

    private static string? stringify(JsonElement? element)
    {
        if (element is not { } e)
            return null;
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            _ => e.GetRawText(),
        };
    }

    private static List<PostmanHeader> BuildEffectiveHeaders(
        PostmanItem item,
        PostmanAuth? auth,
        IReadOnlyDictionary<string, string> variables)
    {
        var headers = new List<PostmanHeader>();
        foreach (var header in item.Headers)
        {
            var value = PostmanParser.ResolveVariables(header.Value, variables);
            if (!string.IsNullOrWhiteSpace(header.Name))
                headers.Add(new PostmanHeader(header.Name, value));
        }

        if (auth is null || auth.Type == PostmanConstants.AuthTypeApiKey)
        {
            if (auth?.Type == PostmanConstants.AuthTypeApiKey &&
                auth.Parameters.TryGetValue(PostmanConstants.AuthParamIn, out var location) &&
                string.Equals(location, "query", StringComparison.OrdinalIgnoreCase))
                return headers;

            var key = auth is null ? null : Param(auth, PostmanConstants.AuthParamKey);
            if (key is not null && !headers.Any(h => string.Equals(h.Name, key, StringComparison.OrdinalIgnoreCase)))
            {
                var value = PostmanParser.ResolveVariables(ParamRequired(auth, PostmanConstants.AuthParamValue), variables);
                headers.Add(new PostmanHeader(key, value));
            }
            return headers;
        }

        if (auth.Type == PostmanConstants.AuthTypeBearer &&
            auth.Parameters.TryGetValue(PostmanConstants.AuthParamToken, out var token) &&
            !headers.Any(h => string.Equals(h.Name, "Authorization", StringComparison.OrdinalIgnoreCase)))
            headers.Add(new PostmanHeader("Authorization", "Bearer " + PostmanParser.ResolveVariables(token, variables)));

        if (auth.Type == PostmanConstants.AuthTypeBasic &&
            auth.Parameters.TryGetValue(PostmanConstants.AuthParamUsername, out var username))
        {
            var password = auth.Parameters.TryGetValue(PostmanConstants.AuthParamPassword, out var p) ? p : "";
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{PostmanParser.ResolveVariables(username, variables)}:{PostmanParser.ResolveVariables(password, variables)}"));
            if (!headers.Any(h => string.Equals(h.Name, "Authorization", StringComparison.OrdinalIgnoreCase)))
                headers.Add(new PostmanHeader("Authorization", "Basic " + credentials));
        }

        return headers;
    }

    private static string? BuildHeadersJson(List<PostmanHeader> headers, string? itemContentType)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
            map[header.Name] = header.Value;

        if (!string.IsNullOrWhiteSpace(itemContentType) &&
            !map.ContainsKey("Content-Type"))
            map["Content-Type"] = itemContentType;

        return map.Count == 0 ? null : JsonSerializer.Serialize(map);
    }

    private static string? Param(PostmanAuth auth, string name) =>
        auth.Parameters.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    private static string ParamRequired(PostmanAuth auth, string name) =>
        auth.Parameters.TryGetValue(name, out var value) ? value : "";

    // Redacts secret auth values when describing what was sent.
    internal static string? AuthFor(PostmanItem item, PostmanAuth? auth, IReadOnlyDictionary<string, string> variables)
    {
        var effective = auth ?? item.Auth;
        if (effective is null)
            return null;
        return effective.Type switch
        {
            PostmanConstants.AuthTypeApiKey when effective.Parameters.TryGetValue(PostmanConstants.AuthParamIn, out var location) && string.Equals(location, "query", StringComparison.OrdinalIgnoreCase) =>
                $"auth: {Param(effective, PostmanConstants.AuthParamKey)}=<redacted> (query)",
            PostmanConstants.AuthTypeApiKey =>
                $"auth: {Param(effective, PostmanConstants.AuthParamKey)}: <redacted> (header)",
            PostmanConstants.AuthTypeBearer =>
                $"auth: Authorization: Bearer <redacted>",
            PostmanConstants.AuthTypeBasic =>
                $"auth: Basic <redacted>",
            _ => null,
        };
    }

    private static System.Diagnostics.Stopwatch StopwatchStart()
    {
        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();
        return sw;
    }

    private static double StopwatchStop(System.Diagnostics.Stopwatch sw)
    {
        sw.Stop();
        return sw.Elapsed.TotalSeconds;
    }
}