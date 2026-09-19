using System.Net;
using System.Text.Json;
using ApiMcp.Helpers;
using Xunit;

namespace ApiMcp.Tests;

public sealed class OpenApiParserTests
{
    [Fact]
    public void OpenApi3_ResolvesRequestBodyAndPathParameters()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        servers:
          - url: https://example.test/api
        paths:
          /pets/{id}:
            parameters:
              - name: id
                in: path
                required: true
                schema:
                  type: integer
            post:
              operationId: updatePet
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/Pet'
        components:
          schemas:
            Pet:
              type: object
              required: [name]
              properties:
                name:
                  type: string
                age:
                  type: integer
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"method\": \"POST\"", json);
        Assert.Contains("\"/pets/{id}\"", json);
        Assert.Contains("\"name\": \"id\"", json);
        Assert.Contains("\"contentType\": \"application/json\"", json);
        Assert.Contains("\"name\": \"string\"", json);
        Assert.Contains("\"age\": 0", json);
    }

    [Fact]
    public void Swagger2_ExposesBodyParameter()
    {
        const string spec = """
        swagger: '2.0'
        info:
          title: Test API
          version: 1.0.0
        host: example.test
        basePath: /v1
        schemes: [https]
        consumes: [application/json]
        paths:
          /pets:
            post:
              parameters:
                - name: body
                  in: body
                  required: true
                  schema:
                    $ref: '#/definitions/Pet'
        definitions:
          Pet:
            type: object
            properties:
              name:
                type: string
              age:
                type: integer
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"method\": \"POST\"", json);
        Assert.Contains("\"/pets\"", json);
        Assert.Contains("\"contentType\": \"application/json\"", json);
        Assert.Contains("\"name\": \"string\"", json);
        Assert.Contains("\"age\": 0", json);
    }

    [Fact]
    public void Swagger2_PathLevelParameterIsIncluded()
    {
        const string spec = """
        swagger: '2.0'
        info:
          title: Test API
          version: 1.0.0
        paths:
          /pets/{id}:
            parameters:
              - name: id
                in: path
                required: true
                type: string
            get:
              responses:
                '200':
                  description: OK
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"name\": \"id\"", json);
        Assert.Contains("\"location\": \"path\"", json);
        Assert.Contains("\"required\": true", json);
    }

    [Fact]
    public void OpenApi3_GeneratesJsonExampleFromNestedSchema()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /orders:
            post:
              requestBody:
                content:
                  application/json:
                    schema:
                      type: object
                      properties:
                        customerId:
                          type: string
                          format: uuid
                        items:
                          type: array
                          items:
                            $ref: '#/components/schemas/Item'
        components:
          schemas:
            Item:
              type: object
              properties:
                productId:
                  type: string
                quantity:
                  type: integer
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("00000000-0000-0000-0000-000000000000", json);
        Assert.Contains("\"items\": [", json);
        Assert.Contains("\"productId\": \"string\"", json);
        Assert.Contains("\"quantity\": 0", json);
    }

    [Fact]
    public void OpenApi3_PreservesExplicitExamplesEnumsAndFormats()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /users:
            get:
              parameters:
                - name: status
                  in: query
                  schema:
                    type: string
                    enum: [active, inactive]
                - name: limit
                  in: query
                  schema:
                    type: integer
                    default: 25
              responses:
                '200':
                  description: OK
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"status\"", json);
        Assert.Contains("\"active\"", json);
        Assert.Contains("\"limit\"", json);
        Assert.Contains("\"example\": 25", json);
    }

    [Fact]
    public void Swagger2_FormData_IsExposedAsExecutableFormRequest()
    {
        const string spec = """
        swagger: '2.0'
        info:
          title: Test API
          version: 1.0.0
        paths:
          /upload:
            post:
              consumes:
                - multipart/form-data
              parameters:
                - name: name
                  in: formData
                  required: true
                  type: string
                - name: file
                  in: formData
                  required: true
                  type: file
              responses:
                '200':
                  description: OK
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"contentType\": \"multipart/form-data\"", json);
        Assert.Contains("\"name\": \"string\"", json);
        Assert.Contains("\"file\"", json);
    }

    [Fact]
    public void InvalidSpecification_ThrowsUsefulError()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenApiParser.Parse("not an OpenAPI document"));

        Assert.Contains("parse", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderedOutput_IsValidJson()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths: {}
        """;

        var json = OpenApiParser.Parse(spec).Render();

        using var document = JsonDocument.Parse(json);

        Assert.Equal("Test API", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("1.0.0", document.RootElement.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("endpoints").ValueKind);
    }

    [Fact]
    public void WhitespaceSpecification_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OpenApiParser.Parse("   \n  "));

        Assert.Contains("'specification' is required.", exception.Message);
    }

    [Fact]
    public void UnresolvableReference_ReportsParseErrors()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /x:
            get:
              responses:
                '200':
                  description: OK
              parameters:
                - name: id
                  in: path
                  required: true
                  schema:
                    $ref: '#/components/schemas/Missing'
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => OpenApiParser.Parse(spec));

        Assert.Contains("OpenAPI/Swagger parse errors:", exception.Message);
    }

    [Fact]
    public void ContentWithoutSchema_HandlesNullSchemas()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /echo:
            post:
              requestBody:
                content:
                  application/json: {}
              responses:
                '200':
                  description: OK
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"contentType\": \"application/json\"", json);
    }

    [Fact]
    public void NumberDateAndBooleanSchemas_GenerateExamples()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /things:
            get:
              responses:
                '200':
                  description: OK
              parameters:
                - name: price
                  in: query
                  schema:
                    type: number
                - name: startDate
                  in: query
                  schema:
                    type: string
                    format: date
                - name: active
                  in: query
                  schema:
                    type: boolean
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"price\"", json);
        Assert.Contains("\"example\": 0", json);
        Assert.Contains("\"2026-01-01\"", json);
        Assert.Contains("\"example\": false", json);
    }

    [Fact]
    public void LongAndDoubleEnums_AreSerializedAsValues()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /measure:
            get:
              responses:
                '200':
                  description: OK
              parameters:
                - name: a
                  in: query
                  schema:
                    type: integer
                    format: int64
                    enum: [9000000000000]
                - name: b
                  in: query
                  schema:
                    type: number
                    enum: [1.5]
                - name: c
                  in: query
                  schema:
                    type: number
                    format: float
                    enum: [2.5]
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("9000000000000", json);
        Assert.Contains("\"format\": \"float\"", json);
        Assert.Contains("2.5", json);
    }

    [Fact]
    public void SchemaLevelExample_IsPreserved()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /ex:
            get:
              responses:
                '200':
                  description: OK
              parameters:
                - name: n
                  in: query
                  schema:
                    type: integer
                    example: 7
        """;

        var json = OpenApiParser.Parse(spec).Render();

        using var document = JsonDocument.Parse(json);
        var parameter = document.RootElement.GetProperty("endpoints")[0].GetProperty("parameters")[0];
        Assert.Equal(7, parameter.GetProperty("example").GetInt32());
    }

    [Fact]
    public void EmptySecurityRequirement_IsNull()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /open:
            get:
              security: []
              responses:
                '200':
                  description: OK
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"security\": null", json);
    }

    [Fact]
    public void BooleanNullArrayAndObjectEnums_AreSerialized()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /ext:
            get:
              responses:
                '200':
                  description: OK
              parameters:
                - name: flag
                  in: query
                  schema:
                    type: boolean
                    enum: [true]
                - name: maybe
                  in: query
                  schema:
                    nullable: true
                    enum: [null]
                - name: list
                  in: query
                  schema:
                    type: array
                    items:
                      type: integer
                    enum: [[1, 2]]
                - name: obj
                  in: query
                  schema:
                    type: object
                    enum: [{ k: 1 }]
        """;

        var json = OpenApiParser.Parse(spec).Render();

        var document = JsonDocument.Parse(json);
        var parameters = document.RootElement.GetProperty("endpoints")[0].GetProperty("parameters");

        var flag = ParameterExample(parameters, "flag");
        Assert.Equal(JsonValueKind.True, flag.ValueKind);

        var maybe = ParameterExample(parameters, "maybe");
        Assert.Equal(JsonValueKind.Null, maybe.ValueKind);

        Assert.Equal([1, 2], ParameterExample(parameters, "list").EnumerateArray().Select(v => v.GetInt32()));

        var obj = ParameterExample(parameters, "obj");
        Assert.Equal(JsonValueKind.Object, obj.ValueKind);
        Assert.Equal(1, obj.GetProperty("k").GetInt32());
    }

    private static JsonElement ParameterExample(JsonElement parameters, string name)
    {
        var parameter = parameters.EnumerateArray().First(p => p.GetProperty("name").GetString() == name);
        return parameter.GetProperty("example");
    }

    [Fact]
    public void ParameterWithExplicitExample_IsPreserved()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /users:
            get:
              responses:
                '200':
                  description: OK
              parameters:
                - name: limit
                  in: query
                  example: 42
                  schema:
                    type: integer
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"example\": 42", json);
    }

    [Fact]
    public void OperationSecurity_IsDescribed()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /secure:
            get:
              security:
                - ApiKeyAuth: []
                - BearerAuth: []
              responses:
                '200':
                  description: OK
        components:
          securitySchemes:
            ApiKeyAuth:
              type: apiKey
              in: header
              name: X-API-Key
            BearerAuth:
              type: http
              scheme: bearer
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"security\": [", json);
        Assert.Contains("\"ApiKeyAuth\"", json);
        Assert.Contains("\"BearerAuth\"", json);
    }

    [Fact]
    public void CyclicReference_IsResolvedWithoutHanging()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /loop:
            post:
              requestBody:
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/Loop'
              responses:
                '200':
                  description: OK
        components:
          schemas:
            Loop:
              $ref: '#/components/schemas/Loop'
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"/loop\"", json);
    }

    [Fact]
    public void NoInfoNoPaths_RendersEmptyDocument()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        servers: []
        paths: {}
        """;

        var json = OpenApiParser.Parse(spec).Render();

        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("endpoints").ValueKind);
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("baseUrls").ValueKind);
    }

    [Fact]
    public void OnlyResponsesMissingError_IsFilteredAndRenders()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /ping:
            get: {}
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"/ping\"", json);
        Assert.Contains("\"responses\": []", json);
        Assert.Contains("\"parameters\": []", json);
    }

    [Fact]
    public void PathLevelParameters_AreIncluded()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /widgets/{id}:
            parameters:
              - name: id
                in: path
                required: true
                schema:
                  type: string
            get:
              responses:
                '200':
                  description: OK
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("\"name\": \"id\"", json);
        Assert.Contains("\"path\"", json);
        Assert.Contains("\"required\": true", json);
    }

    [Fact]
    public void ObjectWithRequiredAndDefault_IsDescribed()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /mix:
            get:
              responses:
                '200':
                  description: OK
              parameters:
                - name: o
                  in: query
                  schema:
                    type: object
                    required: [id]
                    properties:
                      id:
                        type: integer
                - name: q
                  in: query
                  schema:
                    type: string
                    default: hello
        """;

        var json = OpenApiParser.Parse(spec).Render();

        using var document = JsonDocument.Parse(json);
        var parameters = document.RootElement.GetProperty("endpoints")[0].GetProperty("parameters");

        var o = parameters.EnumerateArray().First(p => p.GetProperty("name").GetString() == "o");
        Assert.Equal(["id"], o.GetProperty("schema").GetProperty("required").EnumerateArray().Select(r => r.GetString()));

        var q = parameters.EnumerateArray().First(p => p.GetProperty("name").GetString() == "q");
        Assert.Equal("hello", q.GetProperty("schema").GetProperty("defaultValue").GetString());
        Assert.Equal("hello", q.GetProperty("example").GetString());
    }

    [Fact]
    public void UnknownSecurityRequirement_IsRejected()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /ghost:
            get:
              security:
                - Ghost: []
              responses:
                '200':
                  description: OK
        """;

        var exception = Assert.Throws<InvalidOperationException>(() => OpenApiParser.Parse(spec));

        Assert.Contains("OpenAPI/Swagger parse errors:", exception.Message);
    }

    [Fact]
    public void DateTypedEnum_IsSerializedViaToStringFallback()
    {
        const string spec = """
        openapi: 3.0.3
        info:
          title: Test API
          version: 1.0.0
        paths:
          /dated:
            get:
              responses:
                '200':
                  description: OK
              parameters:
                - name: d
                  in: query
                  schema:
                    type: string
                    format: date
                    enum: [2026-02-03]
        """;

        var json = OpenApiParser.Parse(spec).Render();

        Assert.Contains("Microsoft.OpenApi.Any.OpenApiDate", json);
    }
}

public sealed class PublicOpenApiIntegrationTests
{
    private static readonly Uri Swagger2PetStore =
        new("https://petstore.swagger.io/v2/swagger.json");

    private static readonly Uri OpenApi3PetStore =
        new("https://petstore3.swagger.io/api/v3/openapi.json");

    [Fact]
    public async Task PetStore_Swagger2_ParsesRealDocument()
    {
        using var client = CreateClient();
        var specification = await client.GetStringAsync(Swagger2PetStore);

        var json = OpenApiParser.Parse(specification).Render();

        Assert.Contains("\"/pet\"", json);
        Assert.Contains("\"method\": \"POST\"", json);
        Assert.Contains("\"contentType\": \"application/json\"", json);
    }

    [Fact]
    public async Task PetStore_OpenApi3_ParsesRealDocument()
    {
        using var client = CreateClient();
        var specification = await client.GetStringAsync(OpenApi3PetStore);

        var json = OpenApiParser.Parse(specification).Render();

        Assert.Contains("\"/pet\"", json);
        Assert.Contains("\"method\": \"POST\"", json);
        Assert.Contains("\"contentType\": \"application/json\"", json);
    }

    private static HttpClient CreateClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }
}
