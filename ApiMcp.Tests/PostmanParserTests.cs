using System.Text.Json;
using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public sealed class PostmanParserTests
{
    [Fact]
    public void Parse_ExtractsCollectionInfoVariablesAndItems()
    {
        var collection = PostmanParser.Parse(SamplePostman.BravoCollection);

        Assert.Equal("Collection", collection.Name);
        Assert.Contains("2.1.0", collection.Schema);
        Assert.Equal("https://bravotest.bravosoft.no/integration-api", collection.Variables["base_url_bravo"]);

        var item = Assert.Single(collection.Items);
        Assert.Equal("", item.Folder);
        Assert.Equal("Search Customers", item.Name);
        Assert.Equal("GET", item.Method);
        Assert.Equal("{{base_url_bravo}}/api/Companies", item.Url);
        Assert.Empty(item.Headers);
        Assert.Null(item.Body);
        Assert.Null(item.Auth);

        Assert.Equal(PostmanConstants.AuthTypeApiKey, collection.Auth!.Type);
        Assert.Equal("x-api-key", collection.Auth.Parameters[PostmanConstants.AuthParamKey]);
        Assert.Equal("service_account_secret", collection.Auth.Parameters[PostmanConstants.AuthParamValue]);
        Assert.Equal("header", collection.Auth.Parameters[PostmanConstants.AuthParamIn]);

        Assert.Equal(3, item.Tests.Count);
        Assert.Equal("Status code is 200", item.Tests[0].Name);
        Assert.Equal(200, item.Tests[0].ExpectedStatus);
        Assert.Empty(item.Tests[0].Includes);
        Assert.Equal("Body should have the total records value", item.Tests[1].Name);
        Assert.Null(item.Tests[1].ExpectedStatus);
        Assert.Equal(new[] { "totalCompanies" }, item.Tests[1].Includes);
        Assert.Contains("let totalCompanies = jsonData.records;", item.Script);
    }

    [Fact]
    public void ResolveVariables_ReplacesOnlyKnownTokens()
    {
        var variables = new Dictionary<string, string> { ["a"] = "1" };

        Assert.Equal("plain", PostmanParser.ResolveVariables("plain", variables));
        Assert.Equal("1", PostmanParser.ResolveVariables("{{a}}", variables));
        Assert.Equal("{{b}}", PostmanParser.ResolveVariables("{{b}}", variables));
        Assert.Equal("", PostmanParser.ResolveVariables("", variables));
    }

    [Fact]
    public void Parse_FoldersAreFlattenedWithPath_AndBodiesParsed()
    {
        var collection = PostmanParser.Parse(SamplePostman.NestedAndBodies);

        var deep = collection.Items[0];
        Assert.Equal("Folder", deep.Folder);
        Assert.Equal("Deep", deep.Name);
        Assert.Equal("POST", deep.Method);
        Assert.Equal("https://example.com.api:8080/v1/things?q=a%20b", deep.Url);
        Assert.Equal("X-Trace", Assert.Single(deep.Headers).Name);
        Assert.Equal("{{trace}}", Assert.Single(deep.Headers).Value);
        Assert.Equal("name=widget", deep.Body);
        Assert.Equal("application/x-www-form-urlencoded", deep.ContentType);

        var bare = collection.Items[1];
        Assert.Equal("", bare.Folder);
        Assert.Equal("GET", bare.Method);
        Assert.Equal("{{missing}}/x", bare.Url);
        Assert.Null(bare.ContentType);

        var graphQl = collection.Items[2];
        Assert.Equal("application/json", graphQl.ContentType);
        Assert.Contains("\"query\":\"{ me { name } }\"", graphQl.Body);
        Assert.Contains("\"variables\":\"{\\\"a\\\":1}\"", graphQl.Body);

        var noBody = collection.Items[3];
        Assert.Null(noBody.Body);
        Assert.Null(noBody.ContentType);
        Assert.Empty(noBody.Tests);
    }

    [Fact]
    public void Parse_AuthTypes_AreExtracted()
    {
        var collection = ParseCollectionWithAuth(
            """{ "type": "bearer", "bearer": [ { "key": "token", "value": "t0" } ] }""");
        Assert.Equal(PostmanConstants.AuthTypeBearer, collection.Auth!.Type);
        Assert.Equal("t0", collection.Auth.Parameters["token"]);

        var basic = ParseCollectionWithAuth(
            """{ "type": "basic", "basic": [ { "key": "username", "value": "u" }, { "key": "password", "value": "p" } ] }""");
        Assert.Equal(PostmanConstants.AuthTypeBasic, basic.Auth!.Type);
        Assert.Equal("u", basic.Auth.Parameters["username"]);
        Assert.Equal("p", basic.Auth.Parameters["password"]);

        var unknown = ParseCollectionWithAuth("""{ "type": "oauth2" }""");
        Assert.Equal("oauth2", unknown.Auth!.Type);
        Assert.Empty(unknown.Auth.Parameters);

        var empty = ParseCollectionWithAuth("""{ "type": "" }""");
        Assert.Null(empty.Auth);

        var missing = PostmanParser.Parse("""{ "item": [] }""");
        Assert.Null(missing.Auth);
    }

    [Fact]
    public void Parse_MissingItemOrInfo_ProducesDefaults()
    {
        var empty = PostmanParser.Parse(SamplePostman.NoItemsNoInfo);
        Assert.Equal("Unnamed collection", empty.Name);
        Assert.Equal("", empty.Schema);
        Assert.Empty(empty.Items);
        Assert.Empty(empty.Variables);
        Assert.Null(empty.Auth);
    }

    [Fact]
    public void Parse_VariableWithEmptyKey_IsSkippedAndDefaultsUsed()
    {
        var collection = PostmanParser.Parse(SamplePostman.NestedAndBodies);
        Assert.Equal("https://oops.example", collection.Variables["missing"]);
        Assert.False(collection.Variables.ContainsKey(""));
        Assert.Equal("abc", collection.Variables["trace"]);
    }

    [Theory]
    [InlineData("""{ "mode": "raw", "raw": "{ \"a\": 1 }" }""", "{ \"a\": 1 }", null)]
    [InlineData("""{ "mode": "urlencoded", "urlencoded": [] }""", null, "application/x-www-form-urlencoded")]
    [InlineData("""{ "mode": "formdata", "formdata": [ { "key": "k", "value": "v" } ] }""", "k=v", "application/x-www-form-urlencoded")]
    [InlineData("""{ "mode": "weird" }""", null, null)]
    [InlineData("""{ }""", null, null)]
    public void Parse_RequestBodyModes(string bodyJson, string? expectedBody, string? expectedContentType)
    {
        var json = $$"""
        {
          "item": [ { "name": "R", "request": { "url": { "raw": "https://x/y" }, "body": {{bodyJson}} } } ]
        }
        """;
        var item = Assert.Single(PostmanParser.Parse(json).Items);
        Assert.Equal(expectedBody, item.Body);
        Assert.Equal(expectedContentType, item.ContentType);
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => PostmanParser.Parse("not json"));
    }

    [Fact]
    public void ExtractTests_QuotedLiteralAndNoScript()
    {
        var fromScript = PostmanParser.ExtractTests(
            "pm.test(\"one\", function () { pm.expect(pm.response.text()).to.include(\"hello\"); });");
        var test = Assert.Single(fromScript);
        Assert.Equal(["\"hello\""], test.Includes);

        Assert.Empty(PostmanParser.ExtractTests(""));
        Assert.Empty(PostmanParser.ExtractTests("no pm.test here; let x = 1;"));
    }

    [Fact]
    public void Render_ListsResolvedRequestsAndAuth()
    {
        var collection = PostmanParser.Parse(SamplePostman.BravoCollection);
        var text = PostmanParser.Render(collection);

        Assert.Contains("Postman collection: Collection (1 requests)", text);
        Assert.Contains("base_url_bravo = https://bravotest.bravosoft.no/integration-api", text);
        Assert.Contains("https://bravotest.bravosoft.no/integration-api/api/Companies", text);
        Assert.Contains("Search Customers", text);
        Assert.Contains("Collection auth: api key in header (x-api-key)", text);
        Assert.Contains("\"Status code is 200\", status 200", text);
        Assert.Contains("Body should have the total records value", text);
    }

    [Fact]
    public void Render_MissingAuthAndVariables_IsHandled()
    {
        var text = PostmanParser.Render(PostmanParser.Parse(SamplePostman.NoItemsNoInfo));
        Assert.Contains("(none)", text);
        Assert.Contains("Collection auth: none", text);
        Assert.Contains("(none)", text);
    }

    [Fact]
    public void DescribeAuth_CoversAllTypes()
    {
        Assert.Equal("none", PostmanParser.DescribeAuth(null));
        Assert.Equal("bearer token", PostmanParser.DescribeAuth(new PostmanAuth("bearer", [])));
        Assert.Equal("basic (u)", PostmanParser.DescribeAuth(new PostmanAuth("basic", new() { ["username"] = "u" })));
        Assert.Equal("api key (k)", PostmanParser.DescribeAuth(new PostmanAuth("apikey", new() { ["key"] = "k" })));
        Assert.Equal("api key in query (k)", PostmanParser.DescribeAuth(new PostmanAuth("apikey", new() { ["key"] = "k", ["in"] = "query" })));
        Assert.Equal("api key (<missing>)", PostmanParser.DescribeAuth(new PostmanAuth("apikey", [])));
        Assert.Equal("oauth2", PostmanParser.DescribeAuth(new PostmanAuth("oauth2", [])));
    }

    private static PostmanCollection ParseCollectionWithAuth(string authJson)
    {
        var json = $$"""
        { "item": [], "auth": {{authJson}} }
        """;
        return PostmanParser.Parse(json);
    }
}