# 03 — Multipart File Upload

> Status: **PASS**
> Echo target: https://httpbin.org/anything (returns the multipart it received)

## Request

```
POST https://httpbin.org/anything
multipart:
  fields:  {"title":"my doc"}
  files:   [{"field":"file","contentBase64":"aGVsbG8gd29ybGQ=",
             "fileName":"hello.txt","contentType":"text/plain"}]
```

## Result

`200 OK` (1524 ms) — httpbin echoed back:

```json
{
  "files": { "file": "hello world" },
  "form":  { "title": "my doc" },
  "headers": {
    "Content-Type": "multipart/form-data; boundary=\"facf0dd8-97ec-42e8-9078-c5692d36ba37\"",
    "Content-Length": "349"
  },
  "method": "POST"
}
```

## Coverage

- `contentBase64` file data decoded and sent (base64 `aGVsbG8gd29ybGQ=` = "hello world")
- mixed `fields` + `files` in one body
- multipart boundary + Content-Type auto-generated
- server picked up file bytes, filename, and form field correctly