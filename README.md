# API MCP Server

[![NuGet version](https://img.shields.io/nuget/v/ApiMcp.Dnx.svg)](https://www.nuget.org/packages/ApiMcp.Dnx/)

An MCP server that lets your LLM call HTTP APIs. Built on the official **MCP C# SDK 2.0** (`ModelContextProtocol`) and **.NET 10**, over **stdio**. Send GET / POST / PUT / PATCH / DELETE / HEAD / OPTIONS / QUERY requests with headers, query strings, bodies and file uploads, parse OpenAPI/Swagger documents, and run Postman collections. Secrets stay on the server side and are never exposed to the model.

## Quick start

Requires the **.NET 10 SDK**. Nothing to install — `dnx` runs the published package straight from NuGet.org:

```
dnx ApiMcp.Dnx@1.4.0 --yes
```

Add it to your MCP client:

```json
{
  "mcpServers": {
    "api": {
      "command": "dnx",
      "args": ["ApiMcp.Dnx@1.4.0", "--yes"]
    }
  }
}
```

That is all you need. Now just ask the model to call an API:

> Use `http_request` to GET https://dummyjson.com/products/1 and show me the JSON.

Or register it with the Claude Code CLI in one line:

```
claude mcp add api --scope user -- dnx ApiMcp.Dnx@1.4.0 --yes
```

Configuration is read once at startup: after changing it, restart the MCP server (in Claude Code, start a new session). Config is passed on the command line via `args` and read from the process `env`.

## Tools

| Tool | What it does |
| --- | --- |
| `http_request` | Sends an HTTP request and returns the status, response headers, and body. |
| `parse_openapi` | Parses an OpenAPI/Swagger JSON or YAML document into testable endpoints (parameters, content types, schemas, and a generated example body). |
| `parse_postman_collection` | Parses a Postman collection (v2.1) into its requests, resolved variables, auth, bodies, and embedded `pm.test` assertions. |
| `run_postman_collection` | Runs every request in a Postman collection against the live servers and evaluates the embedded `pm.test` assertions (PASS/FAIL per assertion). |

### `http_request`

| Parameter | Notes |
| --- | --- |
| `method` *(required)* | `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `HEAD`, `OPTIONS`, `QUERY` |
| `url` *(required)* | Absolute URL |
| `headers` | JSON object, e.g. `{"Content-Type": "application/json"}` |
| `query` | Raw query string, appended to the URL as-is, e.g. `"page=1&size=10"` |
| `body` | Raw request body. JSON-looking bodies default to `Content-Type: application/json` |
| `multipart` | JSON object for `multipart/form-data` file uploads (see below) |
| `timeoutSeconds` | Default `30` |
| `followRedirects` | Default `true` |

`QUERY` is a safe, idempotent method for sending a query in the request body (like a read-only POST; RFC 9110 extension). Responses are rendered as status line + response headers + body; bodies over 100 KB are truncated.

```json
{
  "method": "POST",
  "url": "https://api.example.com/orders",
  "headers": { "Content-Type": "application/json" },
  "body": "{\"sku\":\"A1\",\"qty\":2}"
}
```

### Multipart (file upload)

Use `multipart` to send `multipart/form-data` (single or more files) instead of a raw `body`. When `multipart` is set, `body` is ignored and the multipart Content-Type (with its boundary) overrides any you pass:

```
http_request(
  method: "POST",
  url: "https://example.com/upload",
  multipart: {
    "fields": {"title": "my doc"},
    "files": [
      {"field": "file", "path": "C:\\uploads\\a.png", "contentType": "image/png", "fileName": "a.png"},
      {"field": "files", "contentBase64": "aGVsbG8gd29ybGQ=", "fileName": "hello.txt"}
    ]
  }
)
```

Each file entry needs either `path` (a local file on the server) or `contentBase64` (or `contentBase64Url`). `field` (default `file`), `fileName`, and `contentType` (inferred from the extension when omitted) are optional. Repeat the same `field` to upload multiple files under one name.

Paths are read with the server's own permissions. To limit which directories the server may read, set `APIMCP_FILE_ROOTS` to a semicolon- or newline-separated list of allowed directories; any path outside those roots is rejected.

### `parse_openapi`

Use it before testing an unfamiliar API. It accepts either a URL to `swagger.json` / `openapi.json` / YAML, or the raw specification text (`url` or `specification`, exactly one). For each endpoint it returns the method/path, path/query/header parameters, every request-body content type, the request-body schema (including referenced component schemas), and a generated `requestBody.example` ready to send as the `body` argument of `http_request`.

```
parse_openapi(url: "https://example.com/swagger/v1/swagger.json")
```

Public documents to try without your own API:

- Swagger 2.0: `parse_openapi(url: "https://petstore.swagger.io/v2/swagger.json")`
- OpenAPI 3.x: `parse_openapi(url: "https://petstore3.swagger.io/api/v3/openapi.json")`

End-to-end: `parse_openapi` → pick `POST /pet` → copy `requestBody.example` → send it with `http_request`.

### Postman collections

Parse a collection to inspect it, or run it and get per-assertion PASS/FAIL:

```
parse_postman_collection(path: "C:\\work\\collection.json")
run_postman_collection(path: "C:\\work\\collection.json", variables: {"base_url": "http://127.0.0.1:8080"})
```

Provide exactly one source: `specification` (raw JSON), `path` (local file), or `url`. The runner applies the common API-key/bearer/basic auth types automatically, resolves `{{variables}}` in the URL, headers, and body, and supports `pm.collectionVariables/environment/variables/globals.set("name", ...)` so later requests can reuse values. Requests run in order. A request with no recognized `pm.test` assertion is reported as `[NOTEST]` and is not counted as passed.

## Secrets

Secrets are environment variables on the server. The model can *use* them but never *sees* them. There are two mechanisms.

### 1. Server-owned header (`--header NAME`)

Declare a header name the server owns; its value is read from the environment variable named after it (upper-cased, every non-alphanumeric character replaced by `_`). The model passes only the name and any value it sends is ignored.

```json
{
  "mcpServers": {
    "api": {
      "command": "dnx",
      "args": ["ApiMcp.Dnx@1.4.0", "--yes", "--header", "X-Api-Key", "--header", "Authorization"],
      "env": {
        "X_API_KEY": "your-secret-key",
        "AUTHORIZATION": "Bearer your-token"
      }
    }
  }
}
```

The model then calls, for example, `http_request(method: "GET", url: "https://api.example.com/me", headers: {"X-Api-Key": "anything"})` and the server injects the real value. `{"X-Api-Key": null}` sends no such header.

### 2. Inline environment reference (`${NAME}`)

Independently of `--header`, any `${NAME}` in a header value is replaced by the value of the server's environment variable `NAME`. This works anywhere in the value:

```json
{ "headers": { "X-Request-Token": "${REQ_TOKEN}", "Authorization": "Bearer ${TOKEN}" } }
```

If a referenced variable is unset or empty, the request fails with a clear error. A header declared with `--header` takes precedence and reads its own name-derived variable, so `${...}` is ignored for it.

## Build & run from source

```
cd ApiMcp
dotnet build
```

```
# local run with a secret header (set AUTHORIZATION to the token first)
dotnet run -- --header Authorization
```

Logs go to stderr only (stdout is reserved for MCP JSON). Secret values never appear in logs.

## Requirements

- .NET SDK 10 (for building / running from source)
- Optional: Native AOT publish needs a Visual Studio C++ workload — see below.

## Publish

### Managed single-file (framework-dependent)

```
cd ApiMcp
dotnet publish -c Release -r win-x64 /p:PublishAot=false /p:PublishSingleFile=true --self-contained false -o .\bin\publish-single
```

### Native AOT (fast startup, no runtime install)

```
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;" + $env:PATH
dotnet publish -c Release -r win-x64 -o .\bin\publish-aot
```

## NuGet publishing

Pushing a `v*` tag (e.g. `v1.4.0`) triggers `.github/workflows/dnx.yml`, which packs `ApiMcp.Dnx` and publishes it to NuGet.org using **OIDC trusted publishing** (no API key stored in the repo). You can also trigger it manually via *Actions → Run workflow* with an optional `package_version`.

## Testing example (public CRUD API with auth)

[DummyJSON](https://dummyjson.com/docs/auth) is a free API with JWT auth and full CRUD — ideal for trying the server. `emilys` / `emilyspass` is a public demo account.

1. Log in to get an access token:

```
http_request(
  method: "POST",
  url: "https://dummyjson.com/auth/login",
  headers: {"Content-Type": "application/json"},
  body: "{\"username\":\"emilys\",\"password\":\"emilyspass\"}"
)
```

The response body contains `accessToken` (and `refreshToken`). Use it in the next calls.

2. Call an auth-protected endpoint:

```
http_request(
  method: "GET",
  url: "https://dummyjson.com/auth/me",
  headers: {"Authorization": "Bearer eyJhbGciOi..."}
)
```

3. Update an existing product (PUT):

```
http_request(
  method: "PUT",
  url: "https://dummyjson.com/auth/products/1",
  headers: {"Content-Type": "application/json", "Authorization": "Bearer eyJhbGciOi..."},
  body: "{\"price\":101}"
)
```

4. Delete it (DELETE):

```
http_request(
  method: "DELETE",
  url: "https://dummyjson.com/auth/products/1",
  headers: {"Authorization": "Bearer eyJhbGciOi..."}
)
```

> Tip: because the DummyJSON token is dynamic, it is fine to pass it literally. For a **fixed** secret (e.g. a GitHub PAT), declare it once with `--header` so the value never reaches the model:

```
dnx ApiMcp.Dnx@1.4.0 --yes --header Authorization   # reads the token from env AUTHORIZATION
# then call: http_request(method: "GET", url: "https://api.github.com/user", headers: {"Authorization": "ignored"})
```

## License

[MIT](LICENSE)
