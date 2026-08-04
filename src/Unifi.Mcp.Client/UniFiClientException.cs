namespace Unifi.Mcp.Client;

public class UniFiClientException : Exception
{
    public UniFiClientException(
        string message,
        string profileName,
        string relativePath,
        System.Net.HttpStatusCode? statusCode = null,
        bool retryable = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProfileName = profileName;
        RelativePath = relativePath;
        StatusCode = statusCode;
        Retryable = retryable;
    }

    public string ProfileName { get; }

    public string RelativePath { get; }

    public System.Net.HttpStatusCode? StatusCode { get; }

    public bool Retryable { get; }
}

public sealed class UniFiAuthenticationException : UniFiClientException
{
    public UniFiAuthenticationException(
        string message,
        string profileName,
        string relativePath,
        System.Net.HttpStatusCode? statusCode = null,
        bool retryable = false,
        Exception? innerException = null)
        : base(message, profileName, relativePath, statusCode, retryable, innerException)
    {
    }
}
