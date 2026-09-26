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

    // Boolean opt-in flag: `--flag`, `--flag=true|false`, or `--flag <true|false>`.
    // Also true when the env var is set to a truthy value (true/1/yes/y/on).
    // Last occurrence wins; when no flag is passed, the env var decides.
    public static bool LoadBoolFlag(string[] args, string flagName, string envVarName)
    {
        bool? result = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == flagName)
            {
                // `--flag <bool>` consumes the next arg when it looks like a bool value.
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--") && TryParseBool(args[i + 1], out var v))
                {
                    result = v;
                    i++;
                }
                else
                {
                    result = true;
                }
            }
            else if (arg.StartsWith(flagName + "=", StringComparison.Ordinal))
            {
                result = TryParseBool(arg[(flagName.Length + 1)..], out var v) ? v : true;
            }
        }

        return result ?? CheckEnv(envVarName);
    }

    private static bool CheckEnv(string envVarName)
    {
        var raw = Environment.GetEnvironmentVariable(envVarName);
        return raw is not null && TryParseBool(raw.Trim(), out var v) && v;
    }

    internal static bool TryParseBool(string? raw, out bool value)
    {
        if (raw is null)
        {
            value = false;
            return false;
        }
        switch (raw.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
            case "yes":
            case "y":
            case "on":
                value = true;
                return true;
            case "0":
            case "false":
            case "no":
            case "n":
            case "off":
            case "":
                value = false;
                return true;
            default:
                value = false;
                return false;
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