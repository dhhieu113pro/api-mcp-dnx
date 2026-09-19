# 01 — DummyJSON Authenticated CRUD Flow

> Public API: https://dummyjson.com/docs/auth — demo account `emilys` / `emilyspass`
> Status: **PASS (4/4)**

## Step 1 — Login (POST)

```
POST https://dummyjson.com/auth/login
Content-Type: application/json
{"username":"emilys","password":"emilyspass"}
```

Result: `200 OK` (583 ms)
- Refs: `Set-Cookie` accessToken + refreshToken, response body contains `accessToken`
- Verified values: `id: 1`, `username: emilys`, `email: emily.johnson@x.dummyjson.com`

## Step 2 — Auth-protected endpoint (GET)

```
GET https://dummyjson.com/auth/me
Authorization: Bearer <accessToken>
```

Result: `200 OK` (395 ms)
- `firstName: Emily`, `lastName: Johnson`, `role: admin`
- Confirms bearer token from step 1 is valid on an auth-required route

## Step 3 — Update product (PUT)

```
PUT https://dummyjson.com/auth/products/1
Authorization: Bearer <accessToken>
Content-Type: application/json
{"price":101}
```

Result: `200 OK` (389 ms)
- `title: "Essence Mascara Lash Princess"`, `price: 101` (mutated from 9.99)

## Step 4 — Delete product (DELETE)

```
DELETE https://dummyjson.com/auth/products/1
Authorization: Bearer <accessToken>
```

Result: `200 OK` (404 ms)
- `isDeleted: true`, `deletedOn: "2026-09-19T03:01:51.153Z"`

## Coverage

POST / GET / PUT / DELETE against an auth-protected API, token reuse across calls.