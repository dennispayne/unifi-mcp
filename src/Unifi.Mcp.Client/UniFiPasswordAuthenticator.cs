using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Unifi.Mcp.Client;

public sealed class UniFiPasswordAuthenticator : IUniFiAuthenticator
{
    private static readonly string[] BearerTokenPropertyNames = ["accessToken", "token", "bearerToken"];
    private static readonly string[] CsrfPropertyNames = ["csrfToken", "csrf"];
    private static readonly string[] ExpiresAtPropertyNames = ["expiresAt", "expiresOn", "expiry"];
    private static readonly string[] ExpiresInPropertyNames = ["expiresInSeconds", "expiresIn"];

    private readonly TimeProvider _timeProvider;
    private readonly Func<string, string?>? _environmentVariableReader;

    public UniFiPasswordAuthenticator(TimeProvider? timeProvider = null, Func<string, string?>? environmentVariableReader = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _environmentVariableReader = environmentVariableReader;
    }

    public async Task<UniFiSessionToken> AuthenticateAsync(
        UniFiAccessProfileOptions profile,
        IUniFiTransport transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(transport);

        var username = profile.ResolveUsername(_environmentVariableReader);
        var password = profile.ResolvePassword(_environmentVariableReader);
        var loginPath = profile.LoginPath ?? throw new InvalidOperationException($"Profile '{profile.Name}' must configure a login path.");

        using var request = new HttpRequestMessage(HttpMethod.Post, loginPath)
        {
            Content = JsonContent.Create(new
            {
                username,
                password = password.Reveal()
            })
        };

        HttpResponseMessage response;
        try
        {
            response = await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new UniFiAuthenticationException(
                $"UniFi authentication failed for profile '{profile.Name}' before a response was received.",
                profile.Name,
                loginPath,
                retryable: true,
                innerException: exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new UniFiAuthenticationException(
                    $"UniFi authentication failed for profile '{profile.Name}' with status {(int)response.StatusCode}.",
                    profile.Name,
                    loginPath,
                    response.StatusCode,
                    retryable: response.StatusCode is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests or >= System.Net.HttpStatusCode.InternalServerError);
            }

            var payload = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            using var jsonDocument = TryParseJson(payload);

            var csrfToken =
                TryGetHeaderValue(response, "X-CSRF-Token", out var headerCsrfToken) ? headerCsrfToken :
                TryGetHeaderValue(response, "X-Csrf-Token", out headerCsrfToken) ? headerCsrfToken :
                JsonSearch.TryFindString(jsonDocument?.RootElement, CsrfPropertyNames, out var bodyCsrfToken) ? bodyCsrfToken :
                null;

            var bearerToken = JsonSearch.TryFindString(jsonDocument?.RootElement, BearerTokenPropertyNames, out var foundToken)
                ? foundToken
                : null;

            var cookies = SetCookieParser.Parse(response);
            var expiresAt = ResolveExpiration(profile, jsonDocument?.RootElement);

            if (cookies.Count == 0 && string.IsNullOrWhiteSpace(bearerToken))
            {
                throw new UniFiAuthenticationException(
                    $"UniFi authentication succeeded for profile '{profile.Name}', but no reusable session token was returned.",
                    profile.Name,
                    loginPath);
            }

            return new UniFiSessionToken
            {
                ExpiresAt = expiresAt,
                BearerToken = bearerToken,
                Cookies = cookies,
                CsrfToken = csrfToken
            };
        }
    }

    private DateTimeOffset ResolveExpiration(UniFiAccessProfileOptions profile, JsonElement? root)
    {
        if (JsonSearch.TryFindString(root, ExpiresAtPropertyNames, out var textValue)
            && DateTimeOffset.TryParse(textValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedTimestamp))
        {
            return parsedTimestamp;
        }

        if (JsonSearch.TryFindInt32(root, ExpiresInPropertyNames, out var secondsToExpire)
            && secondsToExpire > 0)
        {
            return _timeProvider.GetUtcNow().AddSeconds(secondsToExpire);
        }

        return _timeProvider.GetUtcNow().Add(profile.SessionTtl);
    }

    private static JsonDocument? TryParseJson(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetHeaderValue(HttpResponseMessage response, string headerName, out string? value)
    {
        if (response.Headers.TryGetValues(headerName, out var values))
        {
            value = values.FirstOrDefault(static candidate => !string.IsNullOrWhiteSpace(candidate));
            return !string.IsNullOrWhiteSpace(value);
        }

        value = null;
        return false;
    }
}
