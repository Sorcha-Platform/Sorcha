// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// HTTP message handler that snapshots the server's <c>Date</c> response
/// header into <see cref="IServerClockObserver"/> on every call (Feature 114,
/// T101). Sits in the same pipeline as <see cref="BearerTokenHandler"/> so
/// it observes every authenticated outbound request without callers having
/// to opt in.
/// </summary>
public sealed class ServerClockHandler : DelegatingHandler
{
    private readonly IServerClockObserver _observer;

    /// <summary>Initialises a new instance.</summary>
    public ServerClockHandler(IServerClockObserver observer)
    {
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.Date is { } serverDate)
        {
            _observer.Record(serverDate);
        }
        return response;
    }
}
