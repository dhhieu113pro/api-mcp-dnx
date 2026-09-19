using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public sealed class HeaderStoreTests : IDisposable
{
    private const string TokenEnvVar = "APIMCP_TEST_TOKEN_XYZ";
    private const string KeyEnvVar = "APIMCP_TEST_KEY_XYZ";
    private readonly Dictionary<string, string?> _originalEnv = new();

    public HeaderStoreTests()
    {
        HeaderStore.Clear();
        SaveAndClear(TokenEnvVar);
        SaveAndClear(KeyEnvVar);
        SaveAndClear("APIMCP_HEADER_ENV");
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
    public void LoadFromArgs_MapsSingleEntry()
    {
        HeaderStore.LoadFromArgsAndEnv(["--header-env", $"Authorization={TokenEnvVar}"]);

        Assert.True(HeaderStore.IsSecret("Authorization"));
        Assert.Equal(TokenEnvVar, HeaderStore.SecretMappings["Authorization"]);
    }

    [Fact]
    public void LoadFromArgs_MapsMultipleFlagOccurrences()
    {
        HeaderStore.LoadFromArgsAndEnv(
            ["--header-env", $"Authorization={TokenEnvVar}", "--header-env", $"X-Api-Key={KeyEnvVar}"]);

        Assert.True(HeaderStore.IsSecret("Authorization"));
        Assert.True(HeaderStore.IsSecret("X-Api-Key"));
    }

    [Fact]
    public void LoadFromArgs_StopsAtNextFlag()
    {
        HeaderStore.LoadFromArgsAndEnv(["--header-env", $"Authorization={TokenEnvVar}", "--other"]);

        Assert.True(HeaderStore.IsSecret("Authorization"));
        Assert.Single(HeaderStore.SecretMappings);
    }

    [Fact]
    public void LoadFromArgs_EntryWithoutEquals_IsIgnored()
    {
        HeaderStore.LoadFromArgsAndEnv(["--header-env", "no-equals-here"]);

        Assert.Empty(HeaderStore.SecretMappings);
    }

    [Fact]
    public void LoadFromEnv_SemicolonSeparated_MapsAllEntries()
    {
        Environment.SetEnvironmentVariable("APIMCP_HEADER_ENV", $"Authorization={TokenEnvVar};X-Api-Key={KeyEnvVar}");
        HeaderStore.LoadFromArgsAndEnv([]);

        Assert.True(HeaderStore.IsSecret("authorization"));
        Assert.True(HeaderStore.IsSecret("x-api-key"));
    }

    [Fact]
    public void IsSecret_IsCaseInsensitive()
    {
        HeaderStore.LoadFromArgsAndEnv(["--header-env", $"Authorization={TokenEnvVar}"]);

        Assert.True(HeaderStore.IsSecret("AUTHORIZATION"));
        Assert.True(HeaderStore.IsSecret("authorization"));
    }

    [Fact]
    public void Resolve_ReturnsEnvValue_OverLlmValue()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, "the-secret");
        HeaderStore.LoadFromArgsAndEnv(["--header-env", $"Authorization={TokenEnvVar}"]);

        Assert.Equal("the-secret", HeaderStore.Resolve("Authorization", "forged-by-llm"));
    }

    [Fact]
    public void Resolve_MissingEnvVar_ThrowsUsefulError()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, null);
        HeaderStore.LoadFromArgsAndEnv(["--header-env", $"Authorization={TokenEnvVar}"]);

        var ex = Assert.Throws<InvalidOperationException>(() => HeaderStore.Resolve("Authorization", "anything"));

        Assert.Contains(TokenEnvVar, ex.Message);
        Assert.Contains("not set", ex.Message);
    }

    [Fact]
    public void Resolve_NullLlmValueOnSecret_StillResolvesSecret()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, "from-env");
        HeaderStore.LoadFromArgsAndEnv(["--header-env", $"Authorization={TokenEnvVar}"]);

        Assert.Equal("from-env", HeaderStore.Resolve("Authorization", null));
    }

    [Fact]
    public void Resolve_NonSecret_ReturnsLlmValue()
    {
        Assert.Equal("plain-value", HeaderStore.Resolve("X-Custom", "plain-value"));
        Assert.Null(HeaderStore.Resolve("X-Custom", null));
    }

    [Fact]
    public void SecretNames_AreOrderedCaseInsensitively()
    {
        HeaderStore.LoadFromArgsAndEnv(["--header-env", $"b-header={KeyEnvVar}", "--header-env", $"auth={TokenEnvVar}"]);

        Assert.Equal(["auth", "b-header"], HeaderStore.SecretNames());
    }

    [Fact]
    public void AddMapping_WhitespaceNameOrEnv_IsIgnored()
    {
        HeaderStore.AddMapping("   ", "X");
        HeaderStore.AddMapping("X", "   ");

        Assert.Empty(HeaderStore.SecretMappings);
    }
}