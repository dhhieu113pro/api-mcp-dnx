using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ApiMcp.AI.Tools;
using ApiMcp.Helpers;

HeaderStore.LoadFromArgsAndEnv(args);
QueryParamStore.LoadFromArgsAndEnv(args);
FileAccessPolicy.LoadFromEnv();

if (HeaderStore.SecretMappings.Count > 0)
{
    var masked = string.Join(", ", HeaderStore.SecretMappings.Select(kv => $"{kv.Key} -> env[{kv.Value}]"));
    Console.Error.WriteLine($"[apimcp] secret headers: {masked}");
}
else
{
    Console.Error.WriteLine("[apimcp] no secret headers configured. Use --secret-header NAME (env APIMCP_SECRET_<NAME>), --header-env Name=ENV_VAR or APIMCP_HEADER_ENV.");
}
foreach (var error in HeaderStore.ConfigErrors)
    Console.Error.WriteLine($"[apimcp] WARNING ignored secret header entry: {error}");
if (HeaderStore.AllowPlain)
    Console.Error.WriteLine("[apimcp] plain header override allowed (--allow-plain-headers). Non-empty caller values are sent as-is; null injects the secret.");

if (QueryParamStore.SecretMappings.Count > 0)
{
    var qMasked = string.Join(", ", QueryParamStore.SecretMappings.Select(kv => $"{kv.Key} -> env[{kv.Value}]"));
    Console.Error.WriteLine($"[apimcp] secret query parameters: {qMasked}");
}
else
{
    Console.Error.WriteLine("[apimcp] no secret query parameters configured. Use --query-env Name=ENV_VAR or APIMCP_QUERY_ENV.");
}
if (QueryParamStore.AllowPlain)
    Console.Error.WriteLine("[apimcp] plain query override allowed (--allow-plain-query). Non-empty caller values are kept as-is; empty value injects the secret.");

if (FileAccessPolicy.Enforced)
    Console.Error.WriteLine($"[apimcp] multipart file roots: {string.Join(", ", FileAccessPolicy.Roots)}");
else
    Console.Error.WriteLine("[apimcp] multipart file access unrestricted. Set APIMCP_FILE_ROOTS to restrict allowed directories.");

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    // stdio: all logs to stderr, JSON only on stdout
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    // generic WithTools<T> keeps metadata for trim/AOT (WithToolsFromAssembly does not)
    .WithTools<HttpTool>()
    .WithTools<OpenApiTool>()
    .WithTools<PostmanTool>();

await builder.Build().RunAsync();