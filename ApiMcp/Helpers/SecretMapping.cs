namespace ApiMcp.Helpers;

// Shared parsing for secret mapping configuration.
// Each mapping name is:  --flag "Name=ENV_VARIABLE"  (repeatable)
// or via environment variable:  VARNAME="Name=ENV_A;Other=ENV_B"
//
// For headers:           flag --header-env,  env APIMCP_HEADER_ENV
// For query parameters:  flag --query-env,   env APIMCP_QUERY_ENV
internal static class SecretMapping
{
    public static IEnumerable<(string Name, string EnvVar)> Load(string[] args, string flagName, string envVarName)
    {
        // CLI flag, repeatable; each following arg up to the next "--" is one mapping.
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != flagName)
                continue;
            for (var j = i + 1; j < args.Length; j++)
            {
                if (args[j].StartsWith("--"))
                    break;
                var (name, envVar) = Split(args[j]);
                if (name is not null)
                    yield return (name, envVar!);
            }
        }

        // Environment variable, semicolon/newline separated.
        var fromEnv = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            foreach (var entry in fromEnv.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                var (name, envVar) = Split(entry);
                if (name is not null)
                    yield return (name, envVar!);
            }
        }
    }

    // Returns (null, null) when the entry has no '='.
    public static (string? name, string? envVar) Split(string entry)
    {
        var eq = entry.IndexOf('=');
        if (eq <= 0)
            return (null, null);
        return (entry[..eq].Trim().Trim('\'', '"'), entry[(eq + 1)..].Trim().Trim('\'', '"'));
    }
}