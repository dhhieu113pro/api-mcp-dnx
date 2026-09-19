using System.Reflection;
using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public sealed class ProgramStartupTests : IDisposable
{
    private readonly Dictionary<string, string?> _originalEnv = new();
    private readonly TextWriter _originalOut = Console.Out;
    private readonly TextWriter _originalError = Console.Error;
    private readonly TextReader _originalIn = Console.In;

    public ProgramStartupTests()
    {
        HeaderStore.Clear();
        QueryParamStore.Clear();
        foreach (var name in new[] { "APIMCP_HEADER_ENV", "APIMCP_QUERY_ENV", "APIMCP_FILE_ROOTS" })
        {
            _originalEnv[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        Console.SetIn(_originalIn);
        foreach (var pair in _originalEnv)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        FileAccessPolicy.LoadFromEnv();
        HeaderStore.Clear();
        QueryParamStore.Clear();
    }

    [Fact]
    public async Task Startup_WithoutConfiguration_LogsUnsetHints()
    {
        var (exitLog, writer) = await RunMainAsync();

        Assert.Contains("[apimcp] no secret headers configured.", exitLog);
        Assert.Contains("[apimcp] no secret query parameters configured.", exitLog);
        Assert.Contains("[apimcp] multipart file access unrestricted.", exitLog);
        Assert.NotEqual("", writer.ToString());
    }

    [Fact]
    public async Task Startup_WithConfiguration_LogsMaskedMappings()
    {
        using var roots = new TempDir();
        Environment.SetEnvironmentVariable("APIMCP_HEADER_ENV", $"Authorization=APIMCP_STARTUP_HEADER");
        Environment.SetEnvironmentVariable("APIMCP_QUERY_ENV", $"api_key=APIMCP_STARTUP_QUERY");
        Environment.SetEnvironmentVariable("APIMCP_FILE_ROOTS", roots.Path);
        Environment.SetEnvironmentVariable("APIMCP_STARTUP_HEADER", "h");
        Environment.SetEnvironmentVariable("APIMCP_STARTUP_QUERY", "q");

        var (exitLog, writer) = await RunMainAsync();

        Assert.Contains("[apimcp] secret headers: Authorization -> env[APIMCP_STARTUP_HEADER]", exitLog);
        Assert.Contains("[apimcp] secret query parameters: api_key -> env[APIMCP_STARTUP_QUERY]", exitLog);
        Assert.Contains("[apimcp] multipart file roots: ", exitLog);
        Assert.NotEqual("", writer.ToString());
    }

    private static async Task<(string stderr, System.IO.StringWriter writer)> RunMainAsync()
    {
        var assembly = typeof(HeaderStore).Assembly;
        var programType = assembly.GetType("Program", throwOnError: false)
            ?? throw new InvalidOperationException("Top-level Program type was not found.");
        var main = programType.GetMethod("<Main>$", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("<Main>$ entry point was not found.");

        var writer = new System.IO.StringWriter();
        Console.SetOut(writer);
        Console.SetError(writer);
        Console.SetIn(new StringReader(""));
        Environment.SetEnvironmentVariable("DOTNET_NOLOGO", "1");

        var task = (Task)main.Invoke(null, new object?[] { Array.Empty<string>() })!;
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)));

        Assert.Same(task, completed);
        await task;

        return (writer.ToString(), writer);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "apimcp-startup-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}