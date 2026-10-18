using System.Text.RegularExpressions;

namespace ApiMcp.Helpers;

// Server-owned registry of secret header mappings.
// Each mapping says: when the tool receives a header named <Name>, read its
// value from the process environment variable named after it instead of
// trusting the LLM-supplied value. Secrets never appear in tool params, logs,
// or prompts.
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

    // --header NAME (repeatable) reads the value from the environment variable named after the header.
    public static void LoadFromArgs(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--header")
                continue;
            for (var j = i + 1; j < args.Length && !args[j].StartsWith("--"); j++)
                AddMapping(args[j], ConventionalEnvVar(args[j]));
        }
    }

    // True when the given header name is owned by the server.
    public static bool IsSecret(string name) => SecretMappings.ContainsKey(name);

    // Resolves the value for a request header.
    //  - A header declared with --header always wins; its value is read from the
    //    environment variable named after it, and a missing env var is an error.
    //  - Otherwise, every "${NAME}" in the caller value is replaced by the value
    //    of the environment variable NAME; a missing one is an error.
    //  - Anything else is passed through literally.
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

        if (llmValue is null)
            return null;

        return EnvReference.Replace(llmValue, match =>
        {
            var refVar = match.Groups[1].Value;
            var value = Environment.GetEnvironmentVariable(refVar);
            if (string.IsNullOrEmpty(value))
                throw new InvalidOperationException(
                    $"Header '{name}' value '{llmValue}' references environment variable '{refVar}', but it is not set or is empty.");
            return value;
        });
    }

    private static readonly Regex EnvReference = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    // Environment variable used by --header: "X-API-KEY" -> "X_API_KEY".
    public static string ConventionalEnvVar(string headerName) =>
        new string(headerName.Trim().ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
}
