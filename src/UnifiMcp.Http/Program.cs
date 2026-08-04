using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UnifiMcp.Core;

namespace UnifiMcp.Http;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            var configPath = UnifiMcpRuntimeLoader.ResolveConfigPath(ReadConfigPathArgument(args));
            var runtime = UnifiMcpRuntimeLoader.LoadFromPath(configPath);

            var builder = WebApplication.CreateBuilder(args);
            builder.WebHost.ConfigureKestrel(static options => options.AddServerHeader = false);
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "O ";
            });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            var configuredUrls = Environment.GetEnvironmentVariable("UNIFI_MCP_HTTP_URLS");
            builder.WebHost.UseUrls(string.IsNullOrWhiteSpace(configuredUrls)
                ? "http://127.0.0.1:8765"
                : configuredUrls);

            builder.Services.AddSingleton(runtime);

            var app = builder.Build();
            app.Lifetime.ApplicationStopping.Register(runtime.Dispose);

            var authToken = Environment.GetEnvironmentVariable("UNIFI_MCP_HTTP_AUTH_TOKEN");

            app.MapGet("/", () => Results.Ok(new { name = "unifi-mcp-http", status = "ok" }));
            app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

            app.MapPost("/mcp", async (HttpRequest request, UnifiMcpRuntime hostRuntime, CancellationToken cancellationToken) =>
            {
                if (!IsAuthorized(request, authToken))
                {
                    return Results.Unauthorized();
                }

                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var requestBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    return Results.BadRequest(new McpResponse("2.0", null, null, new McpError(-32600, "Request body is required.")));
                }

                try
                {
                    var responseBody = await hostRuntime.Host.HandleJsonRpcAsync(requestBody, cancellationToken).ConfigureAwait(false);
                    return responseBody is null
                        ? Results.Accepted()
                        : Results.Content(responseBody, "application/json");
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new McpResponse("2.0", null, null, new McpError(-32700, "Invalid JSON-RPC payload.")));
                }
                catch (InvalidOperationException exception)
                {
                    return Results.BadRequest(new McpResponse("2.0", null, null, new McpError(-32600, exception.Message)));
                }
            });

            await app.RunAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Fatal startup failure: {exception.Message}").ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
    }

    private static bool IsAuthorized(HttpRequest request, string? expectedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return true;
        }

        if (!request.Headers.TryGetValue("Authorization", out var authorizationValues))
        {
            return false;
        }

        var headerValue = authorizationValues.ToString();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providedToken = headerValue["Bearer ".Length..].Trim();
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        var providedBytes = Encoding.UTF8.GetBytes(providedToken);

        return expectedBytes.Length == providedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private static string? ReadConfigPathArgument(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], "--config", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count)
            {
                return args[index + 1];
            }

            if (args[index].StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
            {
                return args[index]["--config=".Length..];
            }
        }

        return null;
    }
}
