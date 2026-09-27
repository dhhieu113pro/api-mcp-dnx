using System.Text.Json;
using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public sealed class PostmanRunnerTests
{
    [Fact]
    public void Run_ItemWithoutAssertions_IsReportedAsNoTestAndNotCountedAsPassed()
    {
        using var server = new MiniHttpServer(statusCode: 400, reasonPhrase: "Bad Request");
        var collection = Collection(server, Item("No tests", "{{base}}x"));

        var output = PostmanRunner.Run(collection, null, 10, true);

        Assert.Contains("[NOTEST] No tests", output);
        Assert.Contains("0 passed, 0 failed, 1 without assertions", output);
    }

    [Fact]
    public void Run_ResolvesVariablesInRequestBody()
    {
        using var server = new MiniHttpServer();
        var collection = Collection(server,
            Item("Post", "{{base}}x", method: "POST", body: """{"customerId":"{{customerId}}"}""",
                script: StatusTest(200)));

        PostmanRunner.Run(collection, new Dictionary<string, string> { ["customerId"] = "c-7" }, 10, true);

        Assert.Equal("""{"customerId":"c-7"}""", server.RequestBodyText);
    }

    [Fact]
    public void Run_CollectionVariablesSet_FromResponseJson_IsUsedByLaterRequests()
    {
        using var server = new MiniHttpServer("""{"data":{"id":42}}""", responseContentType: "application/json");
        var collection = Collection(server,
            Item("Create", "{{base}}items", script: StatusTest(200) +
                "\npm.collectionVariables.set(\"workOrderId\", pm.response.json().data.id);"),
            Item("Get", "{{base}}items/{{workOrderId}}", script: StatusTest(200)));

        var output = PostmanRunner.Run(collection, null, 10, true);

        Assert.Contains("/items/42 ", server.RequestLine);
        Assert.Contains("→ set workOrderId = 42", output);
        Assert.Contains("2 passed, 0 failed", output);
    }

    [Fact]
    public void Run_EnvironmentAndVariablesSet_SupportLetAliasAndLiterals()
    {
        using var server = new MiniHttpServer("""{"data":{"id":42}}""", responseContentType: "application/json");
        var collection = Collection(server,
            Item("Create", "{{base}}items", script: StatusTest(200) +
                "\nlet d = pm.response.json();" +
                "\npm.environment.set('a', d.data.id);" +
                "\npm.variables.set(\"b\", \"lit\");"),
            Item("Get", "{{base}}items/{{a}}-{{b}}", script: StatusTest(200)));

        PostmanRunner.Run(collection, null, 10, true);

        Assert.Contains("/items/42-lit ", server.RequestLine);
    }

    [Fact]
    public void Run_VariableSetWithUnresolvablePath_LeavesVariableUnchanged()
    {
        using var server = new MiniHttpServer("""{"data":{"id":42}}""", responseContentType: "application/json");
        var collection = Collection(server,
            Item("Create", "{{base}}items", script: StatusTest(200) +
                "\npm.collectionVariables.set(\"c\", pm.response.json().nope);"),
            Item("Get", "{{base}}items/{{c}}", script: StatusTest(200)));

        var output = PostmanRunner.Run(collection, new Dictionary<string, string> { ["c"] = "old" }, 10, true);

        Assert.Contains("→ could not resolve value for c", output);
        Assert.Contains("/items/old ", server.RequestLine);
    }

    [Fact]
    public void Run_VariableSetWithSecretLookingName_IsRedactedInOutput()
    {
        using var server = new MiniHttpServer("""{"access_token":"s3cr3t"}""", responseContentType: "application/json");
        var collection = Collection(server,
            Item("Login", "{{base}}login", script: StatusTest(200) +
                "\npm.collectionVariables.set(\"authToken\", pm.response.json().access_token);"));

        var output = PostmanRunner.Run(collection, null, 10, true);

        Assert.Contains("→ set authToken = <redacted>", output);
        Assert.DoesNotContain("s3cr3t", output);
    }

    [Fact]
    public void Run_BodyIncludeOfResponseJsonPath_IsEvaluated()
    {
        using var server = new MiniHttpServer("""{"records":15}""", responseContentType: "application/json");
        var collection = Collection(server,
            Item("List", "{{base}}list", script:
                "let jsonData = pm.response.json();\nlet total = jsonData.records;\n" +
                "pm.test(\"Has total\", function () {\n    pm.expect(pm.response.text()).to.include(total);\n});"));

        var output = PostmanRunner.Run(collection, null, 10, true);

        Assert.Contains("✓ \"Has total\" — body contains \"15\"", output);
    }

    [Fact]
    public void Run_BodyIncludeLiteral_ResolvesCollectionVariables()
    {
        using var server = new MiniHttpServer("""{"customerId":"c-7"}""", responseContentType: "application/json");
        var collection = Collection(server,
            Item("Get", "{{base}}customers/{{customerId}}", script:
                "pm.test(\"Body has id\", function () {\n    pm.expect(pm.response.text()).to.include('{{customerId}}');\n});"));

        var output = PostmanRunner.Run(collection, new Dictionary<string, string> { ["customerId"] = "c-7" }, 10, true);

        Assert.Contains("✓ \"Body has id\" — body contains \"c-7\"", output);
    }

    [Fact]
    public void Run_BodyIncludeLiteral_UsesVariablesSetByEarlierRequests()
    {
        using var server = new MiniHttpServer("""{"id":"w-1"}""", responseContentType: "application/json");
        var collection = Collection(server,
            Item("Create", "{{base}}items", script: StatusTest(200) +
                "\npm.collectionVariables.set(\"itemId\", pm.response.json().id);"),
            Item("Get", "{{base}}items/{{itemId}}", script:
                "pm.test(\"Body has id\", function () {\n    pm.expect(pm.response.text()).to.include(\"{{itemId}}\");\n});"));

        var output = PostmanRunner.Run(collection, null, 10, true);

        Assert.Contains("✓ \"Body has id\" — body contains \"w-1\"", output);
    }

    private static string StatusTest(int status) =>
        $"pm.test(\"Status code is {status}\", function () {{\n    pm.response.to.have.status({status});\n}});";

    private static object Item(string name, string url, string method = "GET", string? body = null, string? script = null)
    {
        var request = new Dictionary<string, object> { ["method"] = method, ["url"] = new { raw = url } };
        if (body is not null)
            request["body"] = new { mode = "raw", raw = body };

        var item = new Dictionary<string, object> { ["name"] = name, ["request"] = request };
        if (script is not null)
            item["event"] = new[] { new { listen = "test", script = new { exec = script.Split('\n') } } };
        return item;
    }

    private static PostmanCollection Collection(MiniHttpServer server, params object[] items) =>
        PostmanParser.Parse(JsonSerializer.Serialize(new
        {
            info = new { name = "runner" },
            variable = new[] { new { key = "base", value = server.Url } },
            item = items,
        }));
}
