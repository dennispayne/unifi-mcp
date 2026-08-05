using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UnifiMcp.Core;

namespace UnifiMcp.Http;

internal static class Program
{
    private const long MaxRequestBodyBytes = 64 * 1024;

    public static async Task Main(string[] args)
    {
        try
        {
            var configPath = UnifiMcpRuntimeLoader.ResolveConfigPath(ReadConfigPathArgument(args));
            var runtime = UnifiMcpRuntimeLoader.LoadFromPath(configPath);

            var builder = WebApplication.CreateBuilder(args);
            builder.WebHost.ConfigureKestrel(static options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
            });
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "O ";
            });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);

            var configuredUrls = Environment.GetEnvironmentVariable("UNIFI_MCP_HTTP_URLS");
            var urls = string.IsNullOrWhiteSpace(configuredUrls)
                ? "http://127.0.0.1:8765"
                : configuredUrls;
            var authToken = Environment.GetEnvironmentVariable("UNIFI_MCP_HTTP_AUTH_TOKEN");
            EnsureRemoteBindingsRequireAuthentication(urls, authToken);
            builder.WebHost.UseUrls(urls);

            builder.Services.AddSingleton(runtime);

            var app = builder.Build();
            app.Lifetime.ApplicationStopping.Register(runtime.Dispose);

            var allowedOrigins = ParseAllowedOrigins(Environment.GetEnvironmentVariable("UNIFI_MCP_HTTP_ALLOWED_ORIGINS"));

            app.Use(async (context, next) =>
            {
                if (!context.Request.Headers.TryGetValue("Origin", out var originValues))
                {
                    await next().ConfigureAwait(false);
                    return;
                }

                if (!IsOriginAllowed(context.Request, allowedOrigins))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                context.Response.Headers.AccessControlAllowOrigin = originValues.ToString().Trim().TrimEnd('/');
                context.Response.Headers.Vary = "Origin";
                if (HttpMethods.IsOptions(context.Request.Method) &&
                    context.Request.Path.Equals("/mcp", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Headers.AccessControlAllowMethods = "POST";
                    context.Response.Headers.AccessControlAllowHeaders =
                        "Authorization, Content-Type, Accept, MCP-Protocol-Version, MCP-Session-Id";
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                }

                await next().ConfigureAwait(false);
            });

            app.MapGet("/", () => Results.Ok(new { name = "unifi-mcp-http", status = "ok" }));
            app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

            app.MapPost("/mcp", async (HttpRequest request, UnifiMcpRuntime hostRuntime, CancellationToken cancellationToken) =>
            {
                if (!IsAuthorized(request, authToken))
                {
                    return Results.Unauthorized();
                }

                if (!IsOriginAllowed(request, allowedOrigins))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                if (!IsJsonContentType(request.ContentType))
                {
                    return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }

                if (!AcceptsJson(request))
                {
                    return Results.StatusCode(StatusCodes.Status406NotAcceptable);
                }

                if (request.ContentLength > MaxRequestBodyBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
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

            app.MapGet("/mcp", () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed));

            await app.RunAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Fatal startup failure: {exception.Message}").ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
    }

    internal static bool IsAuthorized(HttpRequest request, string? expectedToken)
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

    internal static bool IsOriginAllowed(HttpRequest request, IReadOnlySet<string> allowedOrigins)
    {
        if (!request.Headers.TryGetValue("Origin", out var originValues))
        {
            return true;
        }

        var origin = originValues.ToString().Trim().TrimEnd('/');
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        return IsLoopbackHost(originUri.Host) || allowedOrigins.Contains(origin);
    }

    internal static bool IsJsonContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) &&
        contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);

    internal static bool AcceptsJson(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Accept", out var acceptValues))
        {
            return false;
        }

        var accept = acceptValues.ToString();
        return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
               accept.Contains("*/*", StringComparison.Ordinal);
    }

    internal static HashSet<string> ParseAllowedOrigins(string? configuredOrigins)
    {
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(configuredOrigins))
        {
            return origins;
        }

        foreach (var value in configuredOrigins.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException($"Invalid HTTP allowed origin '{value}'.");
            }

            origins.Add(value.TrimEnd('/'));
        }

        return origins;
    }

    internal static void EnsureRemoteBindingsRequireAuthentication(string configuredUrls, string? authToken)
    {
        var hasRemoteBinding = configuredUrls
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(IsRemoteBinding);

        if (hasRemoteBinding && string.IsNullOrWhiteSpace(authToken))
        {
            throw new InvalidOperationException("UNIFI_MCP_HTTP_AUTH_TOKEN is required when HTTP binds beyond loopback.");
        }
    }

    internal static bool IsRemoteBinding(string value)
    {
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart >= 0)
        {
            var authority = value[(authorityStart + 3)..];
            if (authority.StartsWith("*:", StringComparison.Ordinal) ||
                authority.StartsWith("+:", StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"Invalid HTTP binding URL '{value}'.");
        }

        return !IsLoopbackHost(uri.Host);
    }

    internal static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address);

    internal static string? ReadConfigPathArgument(IReadOnlyList<string> args)
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
