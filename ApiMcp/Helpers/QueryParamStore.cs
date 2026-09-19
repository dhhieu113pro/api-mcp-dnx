namespace ApiMcp.Helpers;

// Server-owned registry of secret query-string parameters.
// Each mapping says: when the 'query' argument of http_request contains a
// parameter named <Name>, read the real value from the process environment
// variable <EnvVar> instead of trusting the LLM-supplied value. This covers
// APIs that take their key on the query string of a normal request, e.g.
// GET ?api_key=SECRET. Secrets never appear in tool params, logs, or prompts.
internal static class QueryParamStore
{
    // Logical parameter name -> environment variable that holds the secret value.
    public static Dictionary<string, string> SecretMappings { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal static void Clear() => SecretMappings.Clear();

    public static void AddMapping(string name, string envVar)
    {
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(envVar))
            SecretMappings[name.Trim()] = envVar.Trim();
    }

    public static void LoadFromArgsAndEnv(string[] args)
    {
        foreach (var (name, envVar) in SecretMapping.Load(args, "--query-env", "APIMCP_QUERY_ENV"))
            AddMapping(name, envVar);
    }

    public static IReadOnlyList<string> SecretNames() =>
        SecretMappings.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    // Injects secret values into a raw query string like 'a=1&b=2'. Any
    // parameter whose name is a secret mapping has its value overwritten by the
    // environment value (URL-encoded). Unmapped parameters are kept as-is.
    public static string? Apply(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || SecretMappings.Count == 0)
            return query;

        var parts = query.Split('&');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var eq = part.IndexOf('=');
            var key = eq < 0 ? part : part[..eq];

            if (!SecretMappings.TryGetValue(key, out var envVar))
                continue;

            var secret = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrEmpty(secret))
                throw new InvalidOperationException(
                    $"Secret query parameter '{key}' is configured to read environment variable '{envVar}', but '{envVar}' is not set or is empty.");

            parts[i] = key + "=" + Uri.EscapeDataString(secret);
        }

        return string.Join('&', parts);
    }
}