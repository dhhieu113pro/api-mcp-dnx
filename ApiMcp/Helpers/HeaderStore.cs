namespace ApiMcp.Helpers;

// Server-owned registry of secret header mappings.
// Each mapping says: when the tool receives a header named <Name>, read its
// value from the process environment variable <EnvVar> instead of trusting the
// LLM-supplied value. Secrets never appear in tool params, logs, or prompts.
internal static class HeaderStore
{
    // Logical header name -> environment variable that holds the secret value.
    public static Dictionary<string, string> SecretMappings { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal static void Clear() => SecretMappings.Clear();

    public static void AddMapping(string name, string envVar)
    {
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(envVar))
            SecretMappings[name.Trim()] = envVar.Trim();
    }

    public static void LoadFromArgsAndEnv(string[] args)
    {
        // 1) CLI: --header-env "Authorization=API_AUTH_TOKEN" (repeatable)
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--header-env")
                continue;
            for (int j = i + 1; j < args.Length; j++)
            {
                if (args[j].StartsWith("--"))
                    break;
                var (name, envVar) = SplitMapping(args[j]);
                if (name is not null)
                    AddMapping(name, envVar!);
            }
        }

        // 2) Env var: APIMCP_HEADER_ENV="Authorization=API_AUTH_TOKEN;X-Key=API_KEY"
        var fromEnv = Environment.GetEnvironmentVariable("APIMCP_HEADER_ENV");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            foreach (var entry in fromEnv.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                var (name, envVar) = SplitMapping(entry);
                if (name is not null)
                    AddMapping(name, envVar!);
            }
        }
    }

    // Returns (null, null) when the entry has no '='.
    private static (string? name, string? envVar) SplitMapping(string entry)
    {
        var eq = entry.IndexOf('=');
        if (eq <= 0)
            return (null, null);
        return (entry[..eq].Trim().Trim('\'', '"'), entry[(eq + 1)..].Trim().Trim('\'', '"'));
    }

    // True when the given header name is owned by the server.
    public static bool IsSecret(string name) => SecretMappings.ContainsKey(name);

    // Resolves the value for a request header. Secret headers always win over
    // anything the LLM supplied; a missing env var surfaces as a clear error.
    public static string? Resolve(string name, string? llmValue)
    {
        if (SecretMappings.TryGetValue(name, out var envVar))
        {
            var secret = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrEmpty(secret))
                throw new InvalidOperationException(
                    $"Secret header '{name}' is configured to read environment variable '{envVar}', but '{envVar}' is not set or is empty.");
            return secret;
        }
        return llmValue;
    }

    public static IReadOnlyList<string> SecretNames() =>
        SecretMappings.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
}