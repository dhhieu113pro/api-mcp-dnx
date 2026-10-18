using System.Collections.Concurrent;
using System.Text;

namespace ApiMcp.Helpers;

// Process-wide store for values captured from responses and reused in later
// requests with {{name}} templating. Populated by http_request's 'capture'
// parameter; lives for the lifetime of the MCP server process.
internal static class VariableStore
{
    private static readonly ConcurrentDictionary<string, string> Values = new(StringComparer.Ordinal);

    public static void Set(string name, string value) => Values[name] = value;

    public static void Clear() => Values.Clear();

    public static IReadOnlyDictionary<string, string> Snapshot() =>
        new Dictionary<string, string>(Values, StringComparer.Ordinal);

    /// <summary>Replaces every {{name}} with its captured value. An unknown name is an error.</summary>
    public static string Interpolate(string input)
    {
        if (input.IndexOf("{{", StringComparison.Ordinal) < 0)
            return input;

        var sb = new StringBuilder(input.Length);
        var i = 0;
        while (i < input.Length)
        {
            var start = input.IndexOf("{{", i, StringComparison.Ordinal);
            if (start < 0)
            {
                sb.Append(input, i, input.Length - i);
                break;
            }

            sb.Append(input, i, start - i);

            var end = input.IndexOf("}}", start + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                sb.Append(input, start, input.Length - start);
                break;
            }

            var name = input[(start + 2)..end].Trim();
            if (name.Length == 0)
                throw new InvalidOperationException("Empty variable reference '{{}}'.");

            if (!Values.TryGetValue(name, out var value))
                throw new InvalidOperationException(
                    $"Variable '{name}' is not set. Capture it from a response first, e.g. capture: {{\"{name}\": \"$.path\"}}.");

            sb.Append(value);
            i = end + 2;
        }

        return sb.ToString();
    }
}
