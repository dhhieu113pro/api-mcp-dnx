namespace ApiMcp.Helpers;

internal sealed record PostmanCollection(
    string Name,
    string Schema,
    Dictionary<string, string> Variables,
    PostmanAuth? Auth,
    List<PostmanItem> Items);

internal sealed record PostmanAuth(string Type, Dictionary<string, string> Parameters);

internal sealed record PostmanHeader(string Name, string Value);

internal sealed record PostmanItem(
    string Folder,
    string Name,
    string Method,
    string Url,
    List<PostmanHeader> Headers,
    PostmanAuth? Auth,
    string? Body,
    string? ContentType,
    List<PostmanTest> Tests,
    string Script,
    // multipart JSON for http requests ({"fields":{...},"files":[{"field","path"}]}) when the body is formdata with files
    string? Multipart = null);

internal sealed record PostmanTest(string Name, int? ExpectedStatus, List<string> Includes);

internal static class PostmanConstants
{
    public const string AuthTypeApiKey = "apikey";
    public const string AuthTypeBearer = "bearer";
    public const string AuthTypeBasic = "basic";
    public const string AuthParamKey = "key";
    public const string AuthParamValue = "value";
    public const string AuthParamIn = "in";
    public const string AuthParamToken = "token";
    public const string AuthParamUsername = "username";
    public const string AuthParamPassword = "password";
}