# API MCP Server

[![NuGet version](https://img.shields.io/nuget/v/ApiMcp.Dnx.svg)](https://www.nuget.org/packages/ApiMcp.Dnx/)

A [Model Context Protocol](https://modelcontextprotocol.io/) server for calling HTTP APIs. Built on the official **MCP C# SDK 2.0** (`ModelContextProtocol`) and `.NET 10`, over **stdio**. Use it as an API client and testing tool for your LLM tool-calling workflow: the LLM can send GET / POST / PUT / PATCH / DELETE / HEAD / OPTIONS / QUERY requests with custom headers, query strings and bodies — while secrets are sourced from environment variables on the server side and are never exposed to the model.

## Quick start with `dnx`

Requires the .NET 10 SDK. Run the latest published package directly from NuGet.org:

```
dnx ApiMcp.Dnx@1.1.0 --yes
```

Configure it in your MCP client:

```json
{
  "mcpServers": {
    "api": {
      "command": "dnx",
      "args": ["ApiMcp.Dnx@1.1.0", "--yes"],
      "env": {
        "APIMCP_HEADER_ENV": "Authorization=MY_API_TOKEN;X-Api-Key=MY_API_KEY",
        "MY_API_TOKEN": "your-secret-token",
        "MY_API_KEY": "your-secret-key"
      }
    }
  }
}
```

## Tools

| Tool | Description | Parameters |
| --- | --- | --- |
| `http_request` | Sends an HTTP request and returns status, headers, and body. | `method` (required), `url` (required), `headers` (optional JSON object), `query` (optional raw query string), `body` (optional), `multipart` (optional JSON object for file uploads), `timeoutSeconds`, `followRedirects` |
| `list_secret_headers` | Lists the secret header names the server can fill from its environment (never the values). | — |

`http_request` supports `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `HEAD`, `OPTIONS`, and `QUERY`. `QUERY` is a safe, idempotent method for sending a query in the request body (like a read-only POST; RFC 9110 extension). Query strings are appended to the URL as-is; bodies default to `application/json` when they look like JSON. Responses are rendered as status line + response headers + body (bodies over 100 KB are truncated).

### Multipart (file upload)

Use `multipart` to send `multipart/form-data` (single or multiple files) instead of a raw `body` (when set, `body` is ignored and the multipart Content-Type with its boundary overrides any you pass):

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

### Secret headers

Sensitive headers are declared server-side with a **name → environment variable** mapping. When the LLM sends a request that includes one of these header names, the server replaces the value with the one from the mapped environment variable. The model never sees the secret and cannot override it.

Configure mappings either per-process:

```
dnx ApiMcp.Dnx@1.1.0 --yes --header-env "Authorization=MY_API_TOKEN" --header-env "X-Api-Key=MY_API_KEY"
```

or via the `APIMCP_HEADER_ENV` environment variable (semicolon-separated):

```
APIMCP_HEADER_ENV="Authorization=MY_API_TOKEN;X-Api-Key=MY_API_KEY"
```

The LLM discovers these names via `list_secret_headers` and simply includes them in the `headers` object of `http_request`; the running server injects the real value.

## Build & Run

```
cd ApiMcp
dotnet build
```

```
# Windows Auth-free local run
dotnet run -- --header-env "Authorization=MY_API_TOKEN"
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

Pushing a `v*` tag triggers the `.github/workflows/dnx.yml` workflow, which packs `ApiMcp.Dnx` and publishes it to NuGet.org using **OIDC trusted publishing** (no API key stored in the repo). You can also trigger it manually via *Actions → Run workflow* with an optional `package_version`.

## Testing example (public CRUD API with auth)

[DummyJSON](https://dummyjson.com/docs/auth) is a free API with JWT auth and full CRUD — ideal for trying the server. `emilys` / `emilyspass` is a public demo account.

1. Login to get an access token:

```
http_request(
  method: "POST",
  url: "https://dummyjson.com/auth/login",
  headers: {"Content-Type": "application/json"},
  body: "{\"username\":\"emilys\",\"password\":\"emilyspass\"}"
)
```

The response body contains `accessToken` (and `refreshToken`). Use it as a Bearer token in the next calls.

2. Call an auth-protected endpoint (pass the token literally here, or wire `Authorization` through a secret header instead — see below):

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

> Tip: because the token is dynamic, it is fine to pass it literally. For a **fixed** secret (e.g. a GitHub PAT), prefer a secret header mapping so the value never reaches the model:

```
dnx ApiMcp.Dnx@1.1.0 --yes --header-env "Authorization=MY_GITHUB_PAT"
# then call: http_request(method: "GET", url: "https://api.github.com/user", headers: {"Authorization": "ignored"})
```

### Running over `dnx` (no install)

```
dnx ApiMcp.Dnx@1.1.0 --yes
```

## License

[MIT](LICENSE)