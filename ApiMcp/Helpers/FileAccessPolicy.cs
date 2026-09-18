namespace ApiMcp.Helpers;

// Restricts which local files the multipart uploader may read.
// If APIMCP_FILE_ROOTS is set (semicolon/newline separated directories),
// only files under those roots are allowed. If unset, any readable path is
// allowed (the server runs locally with the user's own permissions).
internal static class FileAccessPolicy
{
    private static string[] _roots = [];

    public static void LoadFromEnv()
    {
        var raw = Environment.GetEnvironmentVariable("APIMCP_FILE_ROOTS");
        if (string.IsNullOrWhiteSpace(raw))
        {
            _roots = [];
            return;
        }

        _roots = raw
            .Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Path.GetFullPath(p.Trim().Trim('\'', '"')))
            .ToArray();
    }

    public static bool Enforced => _roots.Length > 0;

    public static IReadOnlyList<string> Roots => _roots;

    public static string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("File 'path' is required.");

        var full = Path.GetFullPath(path.Trim().Trim('\'', '"'));

        if (!File.Exists(full))
            throw new InvalidOperationException($"File not found: {full}");

        if (Enforced && !_roots.Any(root => IsUnder(full, root)))
            throw new InvalidOperationException(
                $"File '{full}' is outside the allowed APIMCP_FILE_ROOTS: {string.Join(", ", _roots)}");

        return full;
    }

    private static bool IsUnder(string file, string root)
    {
        var rel = Path.GetRelativePath(root, file);
        return !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
    }
}