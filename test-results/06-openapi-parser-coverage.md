# 06 — OpenAPI Parser Coverage + 100% Line/Branch

> Status: **PASS (179/179)** — `dotnet test ApiMcp.Tests`
> Coverage: **647/647 lines (100%), 462/462 branches (100%)** — coverlet cobertura
> Date: 2026-09-19

Chased the last coverage gaps in `OpenApiParser` (previously 99.8% lines /
96.3% branches). Got to **100% on both** by combining spec-driven tests with a
reflection-based white-box suite.

## Why white-box tests

OpenApiParser builds its descriptions defensively (null-conditional chains,
`?? []`/ternary fallbacks), but OpenAPI.NET rejects or normalizes the inputs
that would reach some of those paths (undefined security schemes, missing
parameter locations, empty schemas/`format: float` value conversions, ...), so
several branches are unreachable through `Parse`. The white-box suite drives the
private methods directly from hand-built `OpenApiDocument` models via
`Activator.CreateInstance` + `GetMethod(BindingFlags.NonPublic)`.

## Spec-driven additions (`OpenApiParserTests.cs`)

- Whitespace-only spec rejected as unsupported
- Unresolvable `$ref` → parse errors surfaced
- Request-body content without schema → null schema/example
- Number / date / boolean schema examples
- Long / double / **float** enums (`format: float` → `OpenApiFloat`; `format: date`
  → `OpenApiDate` fallback via `ToString`)
- Boolean / null / array / object enums
- Parameter with explicit example preserved
- Operation security described; unknown security scheme rejected
- Cyclic `$ref`s resolve without hanging
- Missing-responses-only parse error filtered (doc still renders)
- Path-level parameters included; object with `required` + `default`
- Schema-level example preserved; empty `security: []` → `null`

## White-box additions (`OpenApiParserWhiteBoxTests.cs`, new)

- Security fallback chain: `Reference?.Id ?? Scheme ?? Name`, including all-null
  scheme, empty requirement, and non-null empty security list
- Parameter with `In = null`; request body with/without `Content`
- Null path-level and operation parameters
- `Resolve`: reference without Id, unresolvable Id, self-referencing schemas,
  null `Components`, null `Components.Schemas`
- `SchemaDescription`: all members, empty enum, null optional members
- `ExampleForSchema`: empty enum, properties-without-type, empty properties on a
  scalar, array, untyped schema, explicit-vs-schema example

## Other fixes in this round

- **Test flake root-caused & fixed**: `AddFile_ByPath_SendsFileContent` failed
  ~40% under the full suite because `FileAccessPolicy._roots` stayed enforced
  after `FileAccessPolicyTests`/`ProgramStartupTests` restored the env var
  without re-reading it. Both `Dispose()` methods now call
  `FileAccessPolicy.LoadFromEnv()`. Verified with repeated full-suite runs.
- Dead-guard removals consistent with earlier approved precedent:
  `OpenApiParser.Parse` null-doc guard, `Render` `Paths`/`Info` null-coalescing,
  `GetParameters` `?[path]`, `HttpHelper.ApplyBody`/`ApplyMultipart` unreachable
  branches — all proved unreachable via probes, removed.
- `MiniHttpServer` diagnostics: `Connections` counter + `RequestTraces` queue.

## Test totals

- **179 passed, 0 failed** (36 at 05 → 179 now; OpenApiParser suite expanded,
  plus new `OpenApiParserWhiteBoxTests`, `ProgramStartupTests`, `HttpToolTests`)
- Command: `dotnet test ApiMcp.Tests --collect:"XPlat Code Coverage" --results-directory TestResults`