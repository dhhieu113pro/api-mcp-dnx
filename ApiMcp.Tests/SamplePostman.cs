namespace ApiMcp.Tests;

internal static class SamplePostman
{
    public const string BravoCollection = """
{
  "info": {
    "_postman_id": "f6ae228c-1865-49af-94bb-10a7f0bfb8a4",
    "name": "Collection",
    "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  "item": [
    {
      "name": "Search Customers",
      "event": [
        {
          "listen": "test",
          "script": {
            "exec": [
              "pm.test(\"Status code is 200\", function () {",
              "    pm.response.to.have.status(200);",
              "});",
              "",
              "let jsonData = pm.response.json();",
              "let totalCompanies = jsonData.records;",
              "pm.test(\"Body should have the total records value\", function () {",
              "    pm.expect(pm.response.text()).to.include(totalCompanies);",
              "});",
              "pm.test(\"Total is shown: \" + totalCompanies, function () {",
              "});"
            ],
            "type": "text/javascript",
            "packages": {}
          }
        }
      ],
      "request": {
        "method": "GET",
        "header": [],
        "url": {
          "raw": "{{base_url_bravo}}/api/Companies",
          "host": ["{{base_url_bravo}}"],
          "path": ["api", "Companies"]
        }
      },
      "response": []
    }
  ],
  "auth": {
    "type": "apikey",
    "apikey": [
      { "key": "value", "value": "service_account_secret", "type": "string" },
      { "key": "key", "value": "x-api-key", "type": "string" },
      { "key": "in", "value": "header", "type": "string" }
    ]
  },
  "event": [
    { "listen": "prerequest", "script": { "type": "text/javascript", "packages": {}, "exec": [""] } }
  ],
  "variable": [
    { "key": "base_url_bravo", "value": "https://bravotest.bravosoft.no/integration-api", "type": "string" }
  ]
}
""";

    public static string CollectionWithBaseUrl(string baseUrl) =>
        BravoCollection.Replace("https://bravotest.bravosoft.no/integration-api", baseUrl, StringComparison.Ordinal);

    /// A minimal collection: single GET, no auth, urlencoded body, a folder nest,
    /// and a test script with a status + include on pm.response.json().totalItems.
    public const string NestedAndBodies = """
{
  "info": { "name": "" },
  "item": [
    {
      "name": "Folder",
      "item": [
        {
          "name": "Deep",
          "request": {
            "method": "POST",
            "header": [ { "key": "", "value": "ignore" }, { "key": "X-Trace", "value": "{{trace}}" } ],
            "url": {
              "protocol": "https",
              "host": ["example.com", "api"],
              "port": "8080",
              "path": ["v1", "things"],
              "query": [ { "key": "q", "value": "a b" } ]
            },
            "body": {
              "mode": "urlencoded",
              "urlencoded": [ { "key": "name", "value": "widget" }, { "key": "", "value": "skip" } ]
            }
          }
        }
      ]
    },
    {
      "name": "Bare",
      "request": { "method": "", "url": { "raw": "{{missing}}/x" } }
    },
    {
      "name": "GraphQl",
      "request": {
        "method": "POST",
        "url": { "raw": "https://example.com/gql" },
        "body": { "mode": "graphql", "graphql": { "query": "{ me { name } }", "variables": "{\"a\":1}" } }
      }
    },
    {
      "name": "NoBody",
      "request": { "url": { "raw": "https://example.com/none" } }
    }
  ],
  "variable": [
    { "key": "missing", "value": "https://oops.example" },
    { "key": "", "value": "orphan" },
    { "key": "trace", "value": "abc" }
  ]
}
""";

    public const string NoItemsNoInfo = """
{ "variable": [] }
""";
}