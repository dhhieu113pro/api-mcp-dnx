using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public sealed class HeaderStoreTests : IDisposable
{
    private const string AuthEnvVar = "AUTHORIZATION";
    private const string KeyEnvVar = "X_API_KEY";
    private const string DynEnvVar = "APIMCP_TEST_DYNAMIC_HEADER";
    private readonly Dictionary<string, string?> _originalEnv = new();

    public HeaderStoreTests()
    {
        HeaderStore.Clear();
        SaveAndClear(AuthEnvVar);
        SaveAndClear(KeyEnvVar);
        SaveAndClear(DynEnvVar);
    }

    public void Dispose()
    {
        HeaderStore.Clear();
        foreach (var pair in _originalEnv)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    }

    private void SaveAndClear(string name)
    {
        _originalEnv[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, null);
    }

    [Fact]
    public void LoadFromArgs_HeaderFlag_MapsToConventionalEnvVar()
    {
        HeaderStore.LoadFromArgs(["--header", "X-API-KEY"]);

        Assert.Equal(KeyEnvVar, HeaderStore.SecretMappings["X-API-KEY"]);
    }

    [Fact]
    public void LoadFromArgs_HeaderFlag_IsRepeatable()
    {
        HeaderStore.LoadFromArgs(["--header", "X-API-KEY", "--header", "Authorization"]);

        Assert.Equal(KeyEnvVar, HeaderStore.SecretMappings["X-API-KEY"]);
        Assert.Equal(AuthEnvVar, HeaderStore.SecretMappings["Authorization"]);
    }

    [Fact]
    public void LoadFromArgs_StopsAtNextFlag()
    {
        HeaderStore.LoadFromArgs(["--header", "Authorization", "--other"]);

        Assert.True(HeaderStore.IsSecret("Authorization"));
        Assert.Single(HeaderStore.SecretMappings);
    }

    [Fact]
    public void LoadFromArgs_HeaderFlag_ResolvesFromConventionalEnvVar()
    {
        Environment.SetEnvironmentVariable(KeyEnvVar, "conventional-secret");
        HeaderStore.LoadFromArgs(["--header", "X-API-KEY"]);

        Assert.Equal("conventional-secret", HeaderStore.Resolve("X-API-KEY", null));
    }

    [Fact]
    public void ConventionalEnvVar_UppercasesAndReplacesNonAlphanumerics()
    {
        Assert.Equal("X_API_KEY", HeaderStore.ConventionalEnvVar("x-api.key"));
    }

    [Fact]
    public void IsSecret_IsCaseInsensitive()
    {
        HeaderStore.LoadFromArgs(["--header", "Authorization"]);

        Assert.True(HeaderStore.IsSecret("AUTHORIZATION"));
        Assert.True(HeaderStore.IsSecret("authorization"));
    }

    [Fact]
    public void Resolve_ReturnsEnvValue_OverLlmValue()
    {
        Environment.SetEnvironmentVariable(AuthEnvVar, "the-secret");
        HeaderStore.LoadFromArgs(["--header", "Authorization"]);

        Assert.Equal("the-secret", HeaderStore.Resolve("Authorization", "forged-by-llm"));
    }

    [Fact]
    public void Resolve_MissingEnvVar_ThrowsUsefulError()
    {
        HeaderStore.LoadFromArgs(["--header", "Authorization"]);

        var ex = Assert.Throws<InvalidOperationException>(() => HeaderStore.Resolve("Authorization", "anything"));

        Assert.Contains(AuthEnvVar, ex.Message);
        Assert.Contains("not set", ex.Message);
    }

    [Fact]
    public void Resolve_NullLlmValueOnSecret_StillResolvesSecret()
    {
        Environment.SetEnvironmentVariable(AuthEnvVar, "from-env");
        HeaderStore.LoadFromArgs(["--header", "Authorization"]);

        Assert.Equal("from-env", HeaderStore.Resolve("Authorization", null));
    }

    [Fact]
    public void Resolve_NonSecret_ReturnsLlmValue()
    {
        Assert.Equal("plain-value", HeaderStore.Resolve("X-Custom", "plain-value"));
        Assert.Null(HeaderStore.Resolve("X-Custom", null));
    }

    [Fact]
    public void Resolve_EnvReference_ReadsEnvironment()
    {
        Environment.SetEnvironmentVariable(DynEnvVar, "from-env");

        Assert.Equal("from-env", HeaderStore.Resolve("X-Custom", "${" + DynEnvVar + "}"));
    }

    [Fact]
    public void Resolve_EnvReference_InterpolatesWithinValue()
    {
        Environment.SetEnvironmentVariable(DynEnvVar, "abc123");

        Assert.Equal("Bearer abc123", HeaderStore.Resolve("X-Custom", "Bearer ${" + DynEnvVar + "}"));
    }

    [Fact]
    public void Resolve_EnvReference_MissingEnvVar_ThrowsUsefulError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HeaderStore.Resolve("X-Custom", "${" + DynEnvVar + "}"));

        Assert.Contains(DynEnvVar, ex.Message);
        Assert.Contains("not set", ex.Message);
    }

    [Fact]
    public void Resolve_UnterminatedReference_IsLiteral()
    {
        Assert.Equal("${", HeaderStore.Resolve("X-Custom", "${"));
        Assert.Equal("$", HeaderStore.Resolve("X-Custom", "$"));
    }

    [Fact]
    public void Resolve_Secret_IgnoresEnvReference()
    {
        Environment.SetEnvironmentVariable(AuthEnvVar, "derived-secret");
        HeaderStore.LoadFromArgs(["--header", "Authorization"]);

        Assert.Equal("derived-secret", HeaderStore.Resolve("Authorization", "${" + DynEnvVar + "}"));
    }

    [Fact]
    public void AddMapping_WhitespaceNameOrEnv_IsIgnored()
    {
        HeaderStore.AddMapping("   ", "X");
        HeaderStore.AddMapping("X", "   ");

        Assert.Empty(HeaderStore.SecretMappings);
    }
}
