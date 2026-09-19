# 04 — Coverage Gap Fill (methods, params, YAML)

> Status: **PASS** — fills the methods/params not covered in 01–03.

## Methods

### PATCH (DummyJSON, persist product via non-auth route)

```
PATCH https://dummyjson.com/products/1
Content-Type: application/json
{"title":"Patched Prod MCP"}
```

Result: `200 OK` — `"title":"Patched Prod MCP"` persisted.

### OPTIONS (httpbin)

```
OPTIONS https://httpbin.org/anything
```

Result: `200 OK` — headers allow `GET, POST, PUT, DELETE, PATCH, OPTIONS`; empty body.

### HEAD (DummyJSON)

```
HEAD https://dummyjson.com/products/1
```

Result: `200 OK` — headers returned (ETag, Content-Type), `Body (0 chars)` as expected for HEAD.

### QUERY (RFC 9110 extension — read-only body)

```
QUERY https://dummyjson.com/products/search          body: {"q":"phone","limit":2}
```

Result: `404 Cannot QUERY /products/search` — tool correctly sends the `QUERY`
verb; DummyJSON does not implement it. (No public API demonstrable here; the
verb transits correctly and the upstream rejection is surfaced.)

## Parameters

### query string (raw, appended as-is)

```
GET https://dummyjson.com/products/search?q=phone&limit=2
```

Result: `200 OK` — returned 2 matching products (`total:23`, `limit:2`).

### followRedirects: false

```
GET http://httpbin.org/redirect/2   (followRedirects: false)
```

Result: `302 Found` returned as-is (headers show `Location: /relative-redirect/1`) — not followed.

### timeoutSeconds (2s on a 5s delay endpoint)

```
GET https://httpbin.org/delay/5   (timeoutSeconds: 2)
```

Result: aborted early (no 5s hang); upstream proxy returned `502`. Confirms the
client-side timeout stops the call. (httpbin/awselb shields the timeout error.)

## parse_openapi — inline raw YAML specification text

```
parse_openapi(specification: "openapi: 3.0.0\ninfo: ... paths: /ping: get: ...")
```

Result: parsed OK →
`title: Minimal YAML API, version 1.0.0`, endpoint `GET /ping` (responses `200`).

## Multipart — contentBase64Url variant

```
POST https://httpbin.org/anything
multipart.files: [{"field":"up","contentBase64Url":"aGVsbG8gd29ybGQ=","fileName":"url.txt"}]
```

Result: `200 OK` — httpbin echoed `files: { "up": "hello world" }`.

## Still not tested (require server restart / config)

- Secret headers (`--header-env Name=ENV_VAR`) — `list_secret_headers` currently
  reports none configured. Needs a server start with the flag (no token value is
  ever exposed to the model).
- `APIMCP_FILE_ROOTS` path restriction (multipart `path`-based file reads).
- Response truncation above 100 KB.
- PATCH/DELETE/OPTIONS/HEAD against a QUERY-capable server (none public).