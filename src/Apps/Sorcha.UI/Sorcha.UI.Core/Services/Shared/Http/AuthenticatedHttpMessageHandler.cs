// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Headers;
using Sorcha.UI.Core.Services.Authentication;
using Sorcha.UI.Core.Services.Configuration;
using Sorcha.UI.Core.Services.Navigation;

namespace Sorcha.UI.Core.Services.Http;

/// <summary>
/// HTTP message handler that injects JWT Bearer tokens and handles token refresh.
/// Redirects to login page with return URL when token refresh fails.
/// </summary>
public class AuthenticatedHttpMessageHandler : DelegatingHandler
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IConfigurationService _configurationService;
    private readonly INavigationService _navigationService;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AuthenticatedHttpMessageHandler(
        IAuthenticationService authenticationService,
        IConfigurationService configurationService,
        INavigationService navigationService)
    {
        _authenticationService = authenticationService;
        _configurationService = configurationService;
        _navigationService = navigationService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Get active profile and token
        var profileName = await _configurationService.GetActiveProfileNameAsync();
        var token = await _authenticationService.GetAccessTokenAsync(profileName);

        // Inject Bearer token if available
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // Send request
        var response = await base.SendAsync(request, cancellationToken);

        // Handle 401 Unauthorized - attempt token refresh even if initial token was null
        // (the refresh token may still be valid in the cache)
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await _refreshLock.WaitAsync(cancellationToken);
            try
            {
                var refreshSucceeded = await _authenticationService.RefreshTokenAsync(profileName);

                if (refreshSucceeded)
                {
                    // Retry with new token — must clone request since HttpRequestMessage can't be resent
                    var newToken = await _authenticationService.GetAccessTokenAsync(profileName);
                    if (!string.IsNullOrEmpty(newToken))
                    {
                        var retryRequest = await CloneRequestAsync(request);
                        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
                        response = await base.SendAsync(retryRequest, cancellationToken);
                    }
                }
                else
                {
                    var returnUrl = _navigationService.CurrentUri;
                    await _navigationService.RedirectToLoginAsync(returnUrl);
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        return response;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        if (original.Content != null)
        {
            var content = await original.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(content);

            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in original.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        clone.Version = original.Version;

        return clone;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshLock?.Dispose();
        }
        base.Dispose(disposing);
    }
}
