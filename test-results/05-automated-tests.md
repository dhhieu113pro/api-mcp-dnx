# 05 — Automated Tests for Previously-Open Items

> Status: **PASS (36/36)** — `dotnet test ApiMcp.Tests`
> Date: 2026-09-19

Covers the three items that could not be exercised live against public APIs.

## New test files

| File | Covers |
|------|--------|
| `ApiMcp.Tests/HeaderStoreTests.cs` | secret headers: `--header-env` mapping, env-var mapping, case-insensitivity, `Resolve` override, missing-env errors |
| `ApiMcp.Tests/FileAccessPolicyTests.cs` | `APIMCP_FILE_ROOTS`: allow under root, reject outside root, multi-root, missing file, no-roots mode |
| `ApiMcp.Tests/HttpHelperTests.cs` | >100 KB response truncation, timeout error, `QUERY` verb transit, query-string append, invalid input errors |

## Bug found & fixed

`HeaderStore.LoadFromArgsAndEnv` had an outer `break` so only the **first**
`--header-env` flag was processed — contradicting the README's "repeatable"
claim. Removed the `break`; multiple `--header-env` flags now all map.

- Repo: `ApiMcp/Helpers/HeaderStore.cs`
- Verified by `LoadFromArgs_MapsMultipleFlagOccurrences` and
  `SecretNames_AreOrderedCaseInsensitively`.

## Test totals

- Total: 36 passed, 0 failed (13 pre-existing OpenAPI parser + 26 new)
- Command: `dotnet test ApiMcp.Tests`