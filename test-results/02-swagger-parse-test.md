# 02 — Swagger / OpenAPI Parse + Test

> Status: **PASS** — parse a spec, pick an endpoint, execute it.

## A. OpenAPI 3.x — Petstore v3

```
parse_openapi(url: "https://petstore3.swagger.io/api/v3/openapi.json")
```

Result: parsed `Swagger Petstore - OpenAPI 3.0` v1.0.27, baseUrl `/api/v3`
- `POST /pet` (addPet) → requestBody example generated:
  `{"id":10,"name":"doggie","photoUrls":["string"],"status":"available",...}`
- Parameters, content types (json / x-www-form-urlencoded / xml), schemas and
  examples returned for every endpoint.

Test `GET /store/inventory` (Swagger 2.0 equivalent) → see below. The v3
inventory endpoint itself returns upstream
`500 {"code":500,"message":"There was an error processing your request...}`
from petstore3.swagger.io — a server-side fault correctly surfaced, not an MCP error.

## B. Swagger 2.0 — Petstore v2

```
parse_openapi(url: "https://petstore.swagger.io/v2/swagger.json")
```

Result: parsed `Swagger Petstore` v1.0.7, baseUrl `https://petstore.swagger.io/v2`

### B1 — Get inventory (GET)

```
GET https://petstore.swagger.io/v2/store/inventory
api_key: special-key
```

Result: `200 OK` (922 ms)
- `{"available":373,"pending":49,"sold":527,...}`

### B2 — Place order (POST) using generated example body

```
POST https://petstore.swagger.io/v2/store/order
Content-Type: application/json
{"complete":false,"id":0,"petId":3,"quantity":2,"shipDate":"2026-09-19T00:00:00Z","status":"placed"}
```

Result: `200 OK` (766 ms)
- `{"id":9223372036854775807,"petId":3,"quantity":2,"shipDate":"2026-09-19T00:00:00.000+0000","status":"placed","complete":false}`

### B3 — Get a non-existent order (GET, error path)

```
GET https://petstore.swagger.io/v2/store/order/1
```

Result: `404 Not Found`
- `{"code":1,"type":"error","message":"Order not found"}` — upstream 404 surfaced intact.

## Coverage

`parse_openapi` JSON (2.0 + 3.x), generated example → `http_request` handoff,
success + error responses.