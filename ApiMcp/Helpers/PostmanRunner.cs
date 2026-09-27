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

    // pm.collectionVariables.set("name", <expr>) and the environment/variables/globals variants.
    [GeneratedRegex(@"pm\.(?:collectionVariables|environment|variables|globals)\.set\(\s*([""'`])([^""'`]+)\1\s*,\s*([^;\n]+?)\s*\)\s*(?:;|$)", RegexOptions.Multiline)]
    private static partial Regex VariableSetRegex();

    [GeneratedRegex(@"token|secret|password|key", RegexOptions.IgnoreCase)]
    private static partial Regex SecretNameRegex();

    private const string NoTestLabel = "[NOTEST]";

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
        int noTest = 0;
        foreach (var item in collection.Items)
        {
            var block = RunItem(item, collection, variables, timeoutSeconds, followRedirects);
            if (block.StartsWith("[PASS]", StringComparison.Ordinal))
                passed++;
            else if (block.StartsWith(NoTestLabel, StringComparison.Ordinal))
                noTest++;
            blocks.Add(block);
        }
        var elapsed = StopwatchStop(total);
        var failed = collection.Items.Count - passed - noTest;
        var noTestText = noTest == 0 ? "" : $", {noTest} without assertions";

        var summary = new StringBuilder();
        summary.AppendLine(
            $"Postman collection: {collection.Name} ({collection.Items.Count} requests) — " +
            $"{passed} passed, {failed} failed{noTestText}, {elapsed:0.###}s");
        foreach (var block in blocks)
            summary.AppendLine(block);
        return summary.ToString();
    }

    private static string RunItem(
        PostmanItem item,
        PostmanCollection collection,
        Dictionary<string, string> variables,
        int timeoutSeconds,
        bool followRedirects)
    {
        var sb = new StringBuilder();
        var url = PostmanParser.ResolveVariables(item.Url, variables);
        var effectiveAuth = item.Auth ?? collection.Auth;
        var headers = BuildEffectiveHeaders(item, effectiveAuth, variables);
        var authDescription = AuthFor(item, effectiveAuth, variables);
        var headersJson = BuildHeadersJson(headers, item.ContentType);
        var body = item.Body is null ? null : PostmanParser.ResolveVariables(item.Body, variables);

        HttpExchange exchange;
        try
        {
            exchange = HttpHelper.Execute(
                item.Method, url, headersJson, null, body,
                multipartJson: null, timeoutSeconds, followRedirects);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[ERROR] {item.Name}");
            sb.AppendLine($"  {item.Method} {url}");
            sb.AppendLine($"  Failed: {ex.Message}");
            return sb.ToString();
        }

        var lets = ParseLets(item.Script);
        var passed = EvaluateTests(item, exchange, lets, variables, out var testLines);
        testLines.AddRange(ApplyVariableSets(item.Script, exchange, lets, variables));

        var label = item.Tests.Count == 0 ? NoTestLabel : passed ? "[PASS]" : "[FAIL]";
        sb.AppendLine($"{label} {item.Name} ({exchange.ElapsedMs}ms)");
        sb.AppendLine($"  {item.Method} {url}");
        if (authDescription is not null)
            sb.AppendLine($"  {authDescription}");
        sb.AppendLine($"  HTTP {exchange.StatusCode} {exchange.ReasonPhrase}");
        foreach (var line in testLines)
            sb.AppendLine($"  {line}");
        return sb.ToString();
    }

    private static Dictionary<string, string> ParseLets(string script)
    {
        var lets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in LetDefinition.Matches(script))
        {
            var name = m.Groups[1].Value;
            var definition = m.Groups[2].Value.Trim();
            if (definition.Length > 0)
                lets[name] = definition;
        }
        return lets;
    }

    // Applies pm.*.set(...) calls in script order so later requests see the values.
    // Only literals and response-JSON paths (directly or via a let alias) are supported.
    private static List<string> ApplyVariableSets(
        string script,
        HttpExchange exchange,
        IReadOnlyDictionary<string, string> lets,
        Dictionary<string, string> variables)
    {
        var lines = new List<string>();
        foreach (Match m in VariableSetRegex().Matches(script))
        {
            var name = m.Groups[2].Value;
            var expression = m.Groups[3].Value;
            var value = ResolveIncludeValue(exchange.Body, expression, lets);
            if (value is null)
            {
                lines.Add($"→ could not resolve value for {name} ({expression}); left unchanged");
                continue;
            }

            variables[name] = value;
            var shown = SecretNameRegex().IsMatch(name) ? "<redacted>" : value;
            lines.Add($"→ set {name} = {shown}");
        }
        return lines;
    }

    private static bool EvaluateTests(
        PostmanItem item,
        HttpExchange exchange,
        IReadOnlyDictionary<string, string> lets,
        IReadOnlyDictionary<string, string> variables,
        out List<string> testLines)
    {
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
                var (ok, includeDetail) = EvaluateInclude(exchange, include, lets, variables);
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
        IReadOnlyDictionary<string, string> lets,
        IReadOnlyDictionary<string, string> variables)
    {
        var value = ResolveIncludeValue(exchange.Body, expression, lets);
        if (value is null)
            return (false, $"could not evaluate body include of '{expression}'");
        // A literal such as '{{customerId}}' is checked against the variable's current value.
        if (Unquote(expression.Trim()) is not null)
            value = PostmanParser.ResolveVariables(value, variables);

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

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            // Clone so the result stays valid after the document is disposed.
            return WalkFromRoot(doc.RootElement, path)?.Clone();
        }
    }

    private static JsonElement? WalkFromRoot(JsonElement root, string path)
    {
        if (root.ValueKind != JsonValueKind.Object && root.ValueKind != JsonValueKind.Array)
            return null;

        JsonElement? current = root;
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