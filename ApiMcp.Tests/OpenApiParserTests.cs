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
