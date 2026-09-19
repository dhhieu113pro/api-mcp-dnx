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
        foreach (var (name, envVar) in SecretMapping.Load(args, "--header-env", "APIMCP_HEADER_ENV"))
            AddMapping(name, envVar);
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