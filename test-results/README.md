# Api MCP Test Results

Run date: 2026-09-19
Environment: opencode session via API MCP tools (`http_request`, `list_secret_headers`, `parse_openapi`)

| # | Scenario | File | Status |
|---|----------|------|--------|
| 1 | DummyJSON authenticated CRUD flow | [01-dummyjson-auth-flow.md](01-dummyjson-auth-flow.md) | PASS |
| 2 | Swagger 2.0 / OpenAPI 3.x parse + test | [02-swagger-parse-test.md](02-swagger-parse-test.md) | PASS |
| 3 | Multipart file upload | [03-multipart-upload.md](03-multipart-upload.md) | PASS |
| 4 | Methods/params gap fill (PATCH, OPTIONS, HEAD, QUERY, query, redirects, timeout, YAML) | [04-coverage-gap-fill.md](04-coverage-gap-fill.md) | PASS |
| 5 | Automated tests for secret headers, FILE_ROOTS, truncation (found + fixed `--header-env` bug) | [05-automated-tests.md](05-automated-tests.md) | PASS |
| 6 | OpenAPI parser coverage push → **100% lines + branches** (179 tests) | [06-openapi-parser-coverage.md](06-openapi-parser-coverage.md) | PASS |

## Coverage

- `dotnet test ApiMcp.Tests --collect:"XPlat Code Coverage" --results-directory TestResults`
  → **647/647 lines (100%), 462/462 branches (100%)** (coverlet cobertura, 2026-09-19).

## Tool checks

- `list_secret_headers` — responding; no secret headers configured
  (`--header-env Name=ENV_VAR` or `APIMCP_HEADER_ENV` not set)
- `list_secret_query_params` — responding; no secret query parameters configured
  (`--query-env Name=ENV_VAR` or `APIMCP_QUERY_ENV` not set)
- `parse_openapi` — parses Swagger 2.0 JSON, OpenAPI 3.x JSON/YAML via Microsoft.OpenApi
- `http_request` — supports GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS/QUERY, custom
  headers, query strings, JSON body, multipart uploads; surfaces upstream errors
  (status line + headers + body)

## Notes

- Petstore v3 `GET /store/inventory` returns upstream 500 (petstore3.swagger.io
  server-side issue; not an MCP fault). Swagger 2.0 equivalent works.
- DummyJSON bearer token is dynamic; use it literally. For fixed secrets prefer
  secret-header mappings (`--header-env`) so values never reach the model.
- `QUERY` verb verified to transit correctly; no public API implements it to
  demonstrate a `200` (expect 404/501 from upstream).
- NOT covered (needs server restart/config): secret headers, `APIMCP_FILE_ROOTS`,
  >100 KB truncation — now covered by automated tests (see [05-automated-tests.md](05-automated-tests.md)),
  including a fix for repeatable `--header-env`.