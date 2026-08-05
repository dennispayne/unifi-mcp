using System.Text.Json;
using UnifiMcp.Core;

namespace UnifiMcp.Stdio;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            if (HasArgument(args, "--create-mutation-approval"))
            {
                await CreateMutationApprovalAsync(args).ConfigureAwait(false);
                return;
            }

            var configPath = UnifiMcpRuntimeLoader.ResolveConfigPath(ReadConfigPathArgument(args));
            using var runtime = UnifiMcpRuntimeLoader.LoadFromPath(configPath);
            await runtime.Host.HandleStdioAsync(Console.OpenStandardInput(), Console.OpenStandardOutput()).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Fatal startup failure: {exception.Message}").ConfigureAwait(false);
            Environment.ExitCode = 1;
        }
    }

    private static async Task CreateMutationApprovalAsync(IReadOnlyList<string> args)
    {
        var scope = ReadRequiredArgument(args, "--scope");
        var method = ReadRequiredArgument(args, "--method").ToUpperInvariant();
        var path = ReadRequiredArgument(args, "--path");
        var keyVariable = ReadOptionalArgument(args, "--key-environment-variable")
            ?? "UNIFI_MCP_MUTATION_APPROVAL_KEY";
        var key = Environment.GetEnvironmentVariable(keyVariable);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"Mutation approval key environment variable '{keyVariable}' is not available.");
        }

        var lifetimeSeconds = int.TryParse(ReadOptionalArgument(args, "--lifetime-seconds"), out var parsedLifetime)
            ? parsedLifetime
            : 120;
        if (lifetimeSeconds is < 30 or > 300)
        {
            throw new InvalidOperationException("Mutation approval lifetime must be from 30 through 300 seconds.");
        }

        byte[]? body = null;
        var bodyPath = ReadOptionalArgument(args, "--body-file");
        if (bodyPath is not null)
        {
            await using var stream = File.OpenRead(bodyPath);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            body = JsonSerializer.SerializeToUtf8Bytes(document.RootElement);
        }

        var token = MutationApprovalToken.Create(
            key,
            scope,
            method,
            path,
            body,
            DateTimeOffset.UtcNow.AddSeconds(lifetimeSeconds));
        await Console.Out.WriteLineAsync(token).ConfigureAwait(false);
    }

    internal static bool HasArgument(IReadOnlyList<string> args, string name) =>
        args.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

    internal static string ReadRequiredArgument(IReadOnlyList<string> args, string name) =>
        ReadOptionalArgument(args, name)
        ?? throw new InvalidOperationException($"Argument '{name}' is required.");

    internal static string? ReadOptionalArgument(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count)
            {
                return args[index + 1];
            }

            var prefix = name + "=";
            if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return args[index][prefix.Length..];
            }
        }

        return null;
    }

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
