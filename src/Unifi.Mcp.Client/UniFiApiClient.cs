namespace Unifi.Mcp.Client;

public sealed class UniFiApiClient : IUniFiApiClient
{
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-CSRF-Token"
    };

    private readonly UniFiAccessProfileOptions _profile;
    private readonly IUniFiTransport _transport;
    private readonly IUniFiAuthenticator _authenticator;
    private readonly IUniFiSessionTokenCache _tokenCache;
    private readonly TimeSpan _refreshSkew;

    public UniFiApiClient(
        UniFiAccessProfileOptions profile,
        IUniFiTransport transport,
        IUniFiAuthenticator authenticator,
        IUniFiSessionTokenCache tokenCache,
        TimeSpan refreshSkew)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _tokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache));
        _refreshSkew = refreshSkew;
    }

    public string ProfileName => _profile.Name;

    public string? ScopeDescription => _profile.ScopeDescription;

    public Task<HttpResponseMessage> SendAsync(UniFiApiRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var allowedPath = UniFiPathScopeGuard.EnsureAllowed(_profile, request.RelativePath);
        var scopedRequest = new UniFiApiRequest(request.Method, allowedPath, request.Body, request.ContentType, request.Headers);
        return SendWithTokenAsync(scopedRequest, allowRetryOnUnauthorized: true, cancellationToken);
    }

    public void Dispose() => _transport.Dispose();

    private async Task<HttpResponseMessage> SendWithTokenAsync(
        UniFiApiRequest request,
        bool allowRetryOnUnauthorized,
        CancellationToken cancellationToken)
    {
        var token = await _tokenCache.GetOrCreateAsync(
            _profile,
            tokenCancellationToken => _authenticator.AuthenticateAsync(_profile, _transport, tokenCancellationToken),
            _refreshSkew,
            cancellationToken).ConfigureAwait(false);

        using var message = BuildRequestMessage(request, token);

        HttpResponseMessage response;
        try
        {
            response = await _transport.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new UniFiClientException(
                $"UniFi request for profile '{_profile.Name}' to '{request.RelativePath}' failed before a response was received.",
                _profile.Name,
                request.RelativePath,
                retryable: true,
                innerException: exception);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && allowRetryOnUnauthorized)
        {
            response.Dispose();
            _tokenCache.Invalidate(_profile.Name);
            return await SendWithTokenAsync(request, allowRetryOnUnauthorized: false, cancellationToken).ConfigureAwait(false);
        }

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = response.StatusCode;
            response.Dispose();
            throw new UniFiClientException(
                $"UniFi request for profile '{_profile.Name}' to '{request.RelativePath}' failed with status {(int)statusCode}.",
                _profile.Name,
                request.RelativePath,
                statusCode,
                retryable: statusCode is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests or >= System.Net.HttpStatusCode.InternalServerError);
        }

        response.RequestMessage = null;
        return response;
    }

    private static HttpRequestMessage BuildRequestMessage(UniFiApiRequest request, UniFiSessionToken token)
    {
        var message = new HttpRequestMessage(request.Method, request.RelativePath);
        var reservedHeaders = new HashSet<string>(ReservedHeaders, StringComparer.OrdinalIgnoreCase);
        foreach (var headerName in token.Headers.Keys)
        {
            reservedHeaders.Add(headerName);
        }

        if (request.Body is not null)
        {
            var content = new ByteArrayContent(request.Body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType!);
            message.Content = content;
        }

        foreach (var header in request.Headers)
        {
            if (reservedHeaders.Contains(header.Key))
            {
                throw new InvalidOperationException($"Header '{header.Key}' is reserved for UniFi authentication and cannot be set by callers.");
            }

            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                if (message.Content is null)
                {
                    throw new InvalidOperationException($"Header '{header.Key}' could not be added to the UniFi request.");
                }

                message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        token.Apply(message);
        return message;
    }
}
