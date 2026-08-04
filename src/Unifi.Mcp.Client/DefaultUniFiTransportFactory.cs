using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Unifi.Mcp.Client;

public sealed class DefaultUniFiTransportFactory : IUniFiTransportFactory
{
    public IUniFiTransport Create(UniFiAccessProfileOptions profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new HttpClientUniFiTransport(profile);
    }
}

internal sealed class HttpClientUniFiTransport : IUniFiTransport
{
    private readonly HttpClient _httpClient;

    public HttpClientUniFiTransport(UniFiAccessProfileOptions profile)
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };

        if (!string.IsNullOrWhiteSpace(profile.PinnedServerCertificateSha256))
        {
            var expectedHash = profile.PinnedServerCertificateSha256.Replace(":", string.Empty, StringComparison.Ordinal).Trim();
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                IsCertificateValid(certificate, errors, expectedHash);
        }

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = EnsureTrailingSlash(profile.BaseAddress),
            Timeout = TimeSpan.FromSeconds(100)
        };
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private static bool IsCertificateValid(X509Certificate? certificate, SslPolicyErrors _, string expectedHash)
    {
        if (certificate is null)
        {
            return false;
        }

        using var certificate2 = new X509Certificate2(certificate);
        var actualHash = Convert.ToHexString(SHA256.HashData(certificate2.RawData));
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(expectedHash),
            Convert.FromHexString(actualHash));
    }

    private static Uri EnsureTrailingSlash(Uri baseAddress)
    {
        var builder = new UriBuilder(baseAddress);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }
}
