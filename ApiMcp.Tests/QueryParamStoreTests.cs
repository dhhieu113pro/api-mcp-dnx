using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public sealed class QueryParamStoreTests : IDisposable
{
    private const string TokenEnvVar = "APIMCP_TEST_QUERY_TOKEN_XYZ";
    private const string KeyEnvVar = "APIMCP_TEST_QUERY_KEY_XYZ";
    private readonly Dictionary<string, string?> _originalEnv = new();

    public QueryParamStoreTests()
    {
        QueryParamStore.Clear();
        SaveAndClear(TokenEnvVar);
        SaveAndClear(KeyEnvVar);
        SaveAndClear("APIMCP_QUERY_ENV");
    }

    public void Dispose()
    {
        QueryParamStore.Clear();
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
        QueryParamStore.LoadFromArgsAndEnv(["--query-env", $"api_key={TokenEnvVar}"]);

        Assert.True(QueryParamStore.SecretMappings.ContainsKey("api_key"));
        Assert.Equal(TokenEnvVar, QueryParamStore.SecretMappings["api_key"]);
    }

    [Fact]
    public void LoadFromArgs_MapsMultipleFlagOccurrences()
    {
        QueryParamStore.LoadFromArgsAndEnv(
            ["--query-env", $"api_key={TokenEnvVar}", "--query-env", $"apikey={KeyEnvVar}"]);

        Assert.Equal(2, QueryParamStore.SecretMappings.Count);
    }

    [Fact]
    public void LoadFromEnv_SemicolonSeparated_MapsAllEntries()
    {
        Environment.SetEnvironmentVariable("APIMCP_QUERY_ENV", $"api_key={TokenEnvVar};apikey={KeyEnvVar}");
        QueryParamStore.LoadFromArgsAndEnv([]);

        Assert.Equal(2, QueryParamStore.SecretMappings.Count);
        Assert.True(QueryParamStore.SecretMappings.ContainsKey("API_KEY"));
    }

    [Fact]
    public void Apply_SubstitutesMappedParam_FromEnvVar()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, "s3cr3t");
        QueryParamStore.LoadFromArgsAndEnv(["--query-env", $"api_key={TokenEnvVar}"]);

        Assert.Equal("api_key=s3cr3t", QueryParamStore.Apply("api_key=__IGNORED__"));
        Assert.Equal("API_KEY=s3cr3t", QueryParamStore.Apply("API_KEY=__IGNORED__"));
    }

    [Fact]
    public void Apply_UrlEncodesSecretValue()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, "a b+c/d?");
        QueryParamStore.LoadFromArgsAndEnv(["--query-env", $"api_key={TokenEnvVar}"]);

        Assert.Equal("api_key=a%20b%2Bc%2Fd%3F", QueryParamStore.Apply("api_key=ignored"));
    }

    [Fact]
    public void Apply_LeavesUnmappedParamsUntouched()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, "v");
        QueryParamStore.LoadFromArgsAndEnv(["--query-env", $"api_key={TokenEnvVar}"]);

        Assert.Equal("a=1&api_key=v&b=2", QueryParamStore.Apply("a=1&api_key=ignored&b=2"));
    }

    [Fact]
    public void Apply_FlagParamWithoutEquals_BecomesKeyValue()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, "v");
        QueryParamStore.LoadFromArgsAndEnv(["--query-env", $"api_key={TokenEnvVar}"]);

        Assert.Equal("api_key=v", QueryParamStore.Apply("api_key"));
    }

    [Fact]
    public void Apply_MissingEnvVar_ThrowsUsefulError()
    {
        Environment.SetEnvironmentVariable(TokenEnvVar, null);
        QueryParamStore.LoadFromArgsAndEnv(["--query-env", $"api_key={TokenEnvVar}"]);

        var ex = Assert.Throws<InvalidOperationException>(() => QueryParamStore.Apply("api_key=whatever"));

        Assert.Contains(TokenEnvVar, ex.Message);
        Assert.Contains("not set", ex.Message);
    }

    [Fact]
    public void Apply_NoMappings_ReturnsQueryAsIs()
    {
        Assert.Equal("a=1&b=2", QueryParamStore.Apply("a=1&b=2"));
    }

    [Fact]
    public void Apply_NullOrWhitespace_ReturnsAsIs()
    {
        Assert.Null(QueryParamStore.Apply(null));
        Assert.Equal("  ", QueryParamStore.Apply("  "));
    }

    [Fact]
    public void SecretNames_AreOrderedCaseInsensitively()
    {
        QueryParamStore.LoadFromArgsAndEnv(
            ["--query-env", $"z-key={KeyEnvVar}", "--query-env", $"api_key={TokenEnvVar}"]);

        Assert.Equal(["api_key", "z-key"], QueryParamStore.SecretNames());
    }

    [Fact]
    public void AddMapping_WhitespaceNameOrEnv_IsIgnored()
    {
        QueryParamStore.AddMapping("   ", "X");
        QueryParamStore.AddMapping("X", "   ");

        Assert.Empty(QueryParamStore.SecretMappings);
    }
}