using UnifiMcp.Core;

namespace UnifiMcp.Stdio;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
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
