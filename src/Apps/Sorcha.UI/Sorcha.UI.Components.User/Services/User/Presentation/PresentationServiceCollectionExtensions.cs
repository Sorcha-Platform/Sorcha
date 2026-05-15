// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Services;

namespace Sorcha.UI.Core.Services.User.Presentation;

/// <summary>
/// Feature 127 — DI registration entry-point for the F127 council-page-side
/// services. Consumer hosts (the Strathcarron sample portal in
/// <c>samples/strathcarron-portal/</c>, or any third-party council
/// deployment) call <see cref="AddSorchaPresentationGate"/> from their
/// <c>Program.cs</c> to wire <see cref="PresentationHubConnection"/> +
/// <see cref="IPresentationSignal"/>.
/// </summary>
public static class PresentationServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="PresentationHubConnection"/> and
    /// <see cref="IPresentationSignal"/>. The hub connects unauthenticated
    /// (council pages have no user session); subscription is keyed by the
    /// high-entropy presentation request id.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="baseAddress">
    /// Gateway origin the consumer page speaks to (e.g.
    /// <c>http://localhost</c>). The hub wrapper appends <c>/hubs/blueprint</c>;
    /// the polling fallback hits <c>/api/presentations/{id}/status</c>
    /// at the same origin.
    /// </param>
    /// <param name="accessTokenProvider">
    /// Optional access-token provider. Pass <c>null</c> for the council-page
    /// case (no user session); third-party councils that DO have a user
    /// session may pass a real provider so the hub subscribes authenticated.
    /// </param>
    public static IServiceCollection AddSorchaPresentationGate(
        this IServiceCollection services,
        string baseAddress,
        Func<IServiceProvider, Func<Task<string?>>?>? accessTokenProvider = null)
    {
        services.AddScoped<PresentationHubConnection>(sp =>
        {
            string hubBaseUrl;
            if (Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri))
            {
                hubBaseUrl = $"{uri.Scheme}://{uri.Authority}";
            }
            else
            {
                hubBaseUrl = baseAddress;
            }

            var logger = sp.GetRequiredService<ILogger<PresentationHubConnection>>();
            var tokenProvider = accessTokenProvider?.Invoke(sp);
            return new PresentationHubConnection(hubBaseUrl, tokenProvider, logger);
        });

        services.AddHttpClient<IPresentationSignal, PresentationSignal>(client =>
        {
            client.BaseAddress = new Uri(baseAddress);
        });

        services.TryAddSingletonTimeProvider();
        return services;
    }

    /// <summary>
    /// Idempotent <see cref="TimeProvider"/> registration — F127 needs it for
    /// the polling fallback timer; other library components may already have
    /// registered it.
    /// </summary>
    private static IServiceCollection TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
        return services;
    }
}
