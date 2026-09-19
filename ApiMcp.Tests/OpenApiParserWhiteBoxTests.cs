using System.Reflection;
using ApiMcp.Helpers;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Xunit;

namespace ApiMcp.Tests;

// White-box tests: OpenApiParser's public surface rejects inputs that would
// trigger some defensive branches (OpenAPI.NET produces parse errors for
// undefined security schemes, missing parameter locations, empty schemas, ...),
// so those branches are driven directly from hand-built OpenApiDocument models.
public sealed class OpenApiParserWhiteBoxTests
{
    [Fact]
    public void SecurityReferenceFallbacks_AreApplied()
    {
        var (doc, op) = MakeDoc();
        var requirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme { Reference = null, Scheme = null, Name = "n" }] = [],
            [new OpenApiSecurityScheme { Reference = null, Scheme = "bearer", Name = null }] = [],
            [new OpenApiSecurityScheme { Reference = null, Scheme = null, Name = null }] = [],
            [new OpenApiSecurityScheme { Reference = new OpenApiReference(), Scheme = "b2", Name = null }] = [],
        };
        op.Security = new List<OpenApiSecurityRequirement> { requirement };

        var result = Invoke("DescribeOperation", doc, "/wb", OperationType.Get, op);
        var security = (object[])Get(result, "security");

        Assert.Equal(new object[] { "n", "bearer", "b2" }, security);
    }

    [Fact]
    public void SecurityRequirement_WithoutSchemes_IsNull()
    {
        var (doc, op) = MakeDoc();
        op.Security = new List<OpenApiSecurityRequirement> { new() };

        var result = Invoke("DescribeOperation", doc, "/wb", OperationType.Get, op);

        Assert.Empty((System.Collections.IEnumerable)Get(result, "security")!);
    }

    [Fact]
    public void ParameterWithoutLocation_IsDescribed()
    {
        var (doc, op) = MakeDoc([
            new OpenApiParameter { Name = "x", In = null, Schema = null, Example = null },
        ]);

        var result = Invoke("DescribeOperation", doc, "/wb", OperationType.Get, op);
        var parameters = (Array)Get(result, "parameters");
        var parameter = parameters.GetValue(0)!;

        Assert.Null(Get(parameter, "location"));
        Assert.Equal("x", Get(parameter, "name"));
    }

    [Fact]
    public void RequestBodyWithoutContent_IsEmpty()
    {
        var (doc, op) = MakeDoc();
        op.RequestBody = new OpenApiRequestBody { Required = false, Content = null };

        var result = Invoke("DescribeOperation", doc, "/wb", OperationType.Get, op);
        object[] requestBodies = ((System.Collections.IEnumerable)Get(result, "requestBodies")!).Cast<object?>().ToArray()!;

        Assert.Empty(requestBodies);
        Assert.Null(Get(result, "requestBody"));
    }

    [Fact]
    public void RequestBodyWithContent_IsDescribed()
    {
        var (doc, op) = MakeDoc();
        op.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = null, Example = null },
            },
        };

        var result = Invoke("DescribeOperation", doc, "/wb", OperationType.Get, op);
        object[] requestBodies = ((System.Collections.IEnumerable)Get(result, "requestBodies")!).Cast<object?>().ToArray()!;

        Assert.Single(requestBodies);
        Assert.Null(Get(requestBodies[0], "schema"));
        Assert.Null(Get(requestBodies[0], "example"));
    }

    [Fact]
    public void NullPathAndOperationParameters_AreHandled()
    {
        var (doc, op) = MakeDoc();
        doc.Paths["/wb"].Parameters = null;
        op.Parameters = null;

        var result = Invoke("DescribeOperation", doc, "/wb", OperationType.Get, op);

        Assert.Empty((Array)Get(result, "parameters"));
    }

    [Fact]
    public void NullResponses_AreHandledAsEmpty()
    {
        var (doc, op) = MakeDoc();
        op.Responses = null;

        var result = Invoke("DescribeOperation", doc, "/wb", OperationType.Get, op);
        var responses = (Array)Get(result, "responses");

        Assert.Empty(responses);
    }

    [Fact]
    public void EmptyNonNullSecurityList_IsNull()
    {
        var (doc, op) = MakeDoc();
        op.Security = new List<OpenApiSecurityRequirement>();

        var result = Invoke("DescribeOperation", doc, "/wb", OperationType.Get, op);

        Assert.Null(Get(result, "security"));
    }

    [Fact]
    public void SchemaDescription_NullOptionalMembers_AreDescribed()
    {
        var (doc, _) = MakeDoc();
        var schema = new OpenApiSchema { Type = "string", Required = null, Properties = null, Items = null, Enum = null, Default = null };

        var result = (object?)Invoke("SchemaDescription", doc, schema);

        Assert.NotNull(result);
        Assert.Equal("string", Get(result, "type"));
        Assert.Null(Get(result, "required"));
        Assert.Null(Get(result, "properties"));
        Assert.Null(Get(result, "items"));
        Assert.Null(Get(result, "enumValues"));
        Assert.Null(Get(result, "defaultValue"));
    }

    [Fact]
    public void DescribeOperation_NullRequestBody_IsNull()
    {
        var (doc, op) = MakeDoc();
        op.RequestBody = null;

        var result = Invoke("DescribeOperation", doc, "/wb", OperationType.Get, op);

        Assert.Null(Get(result, "requestBody"));
        Assert.Empty((System.Collections.IEnumerable)Get(result, "requestBodies")!);
    }

    [Fact]
    public void DescribeOperation_NullSecurity_IsNull()
    {
        var (doc, op) = MakeDoc();
        op.Security = null;

        var result = Invoke("DescribeOperation", doc, "/wb", OperationType.Get, op);

        Assert.Null(Get(result, "security"));
    }

    [Fact]
    public void Resolve_ReferenceWithoutId_IsReturned()
    {
        var (doc, _) = MakeDoc();
        var input = new OpenApiSchema { Reference = new OpenApiReference() };

        var result = (OpenApiSchema)Invoke("Resolve", doc, input);

        Assert.Same(input, result);
    }

    [Fact]
    public void Resolve_UnresolvableReference_IsReturned()
    {
        var (doc, _) = MakeDoc();
        var input = new OpenApiSchema { Reference = new OpenApiReference { Id = "Ghost" } };

        var result = (OpenApiSchema)Invoke("Resolve", doc, input);

        Assert.Same(input, result);
    }

    [Fact]
    public void Resolve_SelfReferencingSchema_Terminates()
    {
        var (doc, _) = MakeDoc();
        var loop = new OpenApiSchema { Reference = new OpenApiReference { Id = "Loop" } };
        doc.Components.Schemas.Add("Loop", loop);
        var input = new OpenApiSchema { Reference = new OpenApiReference { Id = "Loop" } };

        var result = (OpenApiSchema)Invoke("Resolve", doc, input);

        Assert.Same(loop, result);
    }

    [Fact]
    public void Resolve_NullComponents_IsReturnedAsIs()
    {
        var (doc, _) = MakeDoc();
        doc.Components = null;
        var input = new OpenApiSchema { Reference = new OpenApiReference { Id = "Loop" } };

        var result = (OpenApiSchema)Invoke("Resolve", doc, input);

        Assert.Same(input, result);
    }

    [Fact]
    public void Resolve_NullComponentsSchemas_IsReturnedAsIs()
    {
        var (doc, _) = MakeDoc();
        doc.Components.Schemas = null;
        var input = new OpenApiSchema { Reference = new OpenApiReference { Id = "Loop" } };

        var result = (OpenApiSchema)Invoke("Resolve", doc, input);

        Assert.Same(input, result);
    }

    [Fact]
    public void SchemaDescription_AllMembers_AreDescribed()
    {
        var (doc, _) = MakeDoc();
        var schema = new OpenApiSchema
        {
            Type = "object",
            Required = new HashSet<string> { "id" },
            Properties = new Dictionary<string, OpenApiSchema> { ["id"] = new() { Type = "string" } },
            Items = new OpenApiSchema { Type = "integer" },
            Enum = [new OpenApiString("a")],
            Default = new OpenApiString("d"),
        };

        var result = (object?)Invoke("SchemaDescription", doc, schema);

        Assert.NotNull(result);
        Assert.Equal("object", Get(result, "type"));
        Assert.Equal(["id"], (string[])(Array)Get(result, "required")!);
        Assert.NotEmpty((System.Collections.IDictionary)Get(result, "properties")!);
        Assert.NotNull(Get(result, "items"));
        Assert.Equal(["a"], (object[])(Array)Get(result, "enumValues")!);
        Assert.Equal("d", Get(result, "defaultValue"));
    }

    [Fact]
    public void SchemaDescription_EmptyEnum_IsDescribed()
    {
        var (doc, _) = MakeDoc();
        var schema = new OpenApiSchema { Enum = [] };

        var result = (object?)Invoke("SchemaDescription", doc, schema);

        Assert.NotNull(result);
        Assert.Empty((Array)Get(result, "enumValues")!);
    }

    [Fact]
    public void ExampleForSchema_EmptyEnum_FallsThrough()
    {
        var (doc, _) = MakeDoc();
        var schema = new OpenApiSchema { Enum = [] };

        var result = Invoke("ExampleForSchema", doc, schema, null);

        Assert.Null(result);
    }

    [Fact]
    public void ExampleForSchema_PropertiesWithoutType_BuildsObject()
    {
        var (doc, _) = MakeDoc();
        var schema = new OpenApiSchema
        {
            Properties = new Dictionary<string, OpenApiSchema> { ["p"] = new() { Type = "string" } },
        };

        var result = Invoke("ExampleForSchema", doc, schema, null);

        Assert.Equal(new Dictionary<string, object?> { ["p"] = "string" }, result);
    }

    [Fact]
    public void ExampleForSchema_EmptyPropertiesOnScalar_FallsThrough()
    {
        var (doc, _) = MakeDoc();
        var schema = new OpenApiSchema { Type = "string", Properties = new Dictionary<string, OpenApiSchema>() };

        var result = Invoke("ExampleForSchema", doc, schema, null);

        Assert.Equal("string", result);
    }

    [Fact]
    public void ExampleForSchema_Array_FallsThrough()
    {
        var (doc, _) = MakeDoc();
        var schema = new OpenApiSchema
        {
            Type = "array",
            Items = new OpenApiSchema { Type = "string" },
        };

        var result = Invoke("ExampleForSchema", doc, schema, null);

        Assert.Equal(new object?[] { "string" }, result);
    }

    [Fact]
    public void ExampleForSchema_UntypedAndEmpty_FallsThrough()
    {
        var (doc, _) = MakeDoc();
        var schema = new OpenApiSchema { Type = null, Properties = null, Enum = null };

        var result = Invoke("ExampleForSchema", doc, schema, null);

        Assert.Null(result);
    }

    [Fact]
    public void ExampleForSchema_ExplicitAndSchemaExample_AreUsed()
    {
        var (doc, _) = MakeDoc();
        var schema = new OpenApiSchema { Type = "integer", Example = new OpenApiInteger(3) };

        var explicitResult = Invoke("ExampleForSchema", doc, schema, new OpenApiString("z"));
        var schemaResult = Invoke("ExampleForSchema", doc, schema, null);

        Assert.Equal("z", explicitResult);
        Assert.Equal(3, schemaResult);
    }

    private static (OpenApiDocument Doc, OpenApiOperation Op) MakeDoc(List<OpenApiParameter>? parameters = null)
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "1.0.0" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, OpenApiSchema>(),
            },
            Paths = new OpenApiPaths(),
        };

        var op = new OpenApiOperation
        {
            Parameters = parameters,
            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } },
        };

        var pathItem = new OpenApiPathItem();
        pathItem.Operations.Add(OperationType.Get, op);
        doc.Paths.Add("/wb", pathItem);

        return (doc, op);
    }

    private static object? Invoke(string methodName, OpenApiDocument doc, params object?[] args)
    {
        var parser = Activator.CreateInstance(
            typeof(OpenApiParser),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [doc],
            culture: null)!;
        var method = typeof(OpenApiParser).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        return method.Invoke(parser, args);
    }

    private static object? Get(object target, string propertyName)
        => target.GetType().GetProperty(propertyName)?.GetValue(target);
}