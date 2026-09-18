using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ApiMcp.AI.Tools;
using ApiMcp.Helpers;

HeaderStore.LoadFromArgsAndEnv(args);
FileAccessPolicy.LoadFromEnv();

if (HeaderStore.SecretMappings.Count > 0)
{
    var masked = string.Join(", ", HeaderStore.SecretMappings.Select(kv => $"{kv.Key} -> env[{kv.Value}]"));
    Console.Error.WriteLine($"[apimcp] secret headers: {masked}");
}
else
{
    Console.Error.WriteLine("[apimcp] no secret headers configured. Use --header-env Name=ENV_VAR or APIMCP_HEADER_ENV.");
}

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
    .WithTools<OpenApiTool>();

await builder.Build().RunAsync();