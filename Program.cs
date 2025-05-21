using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(o =>           // stdout ↔ stderr split helps MCP tooling
    o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer(server =>
    {
        server.ServerInfo = new()           // ensure the object exists
        {
            Name    = "MathMCP",
            Version = "0.1.0"
        };
    })
    .WithStdioServerTransport()            // easiest transport while developing :contentReference[oaicite:3]{index=3}
    .WithToolsFromAssembly();              // auto-register all [McpServerToolType] tools
                                           // (picks up MathTools)
await builder.Build().RunAsync();
