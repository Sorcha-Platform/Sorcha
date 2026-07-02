// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.Wallet.Pwa.Services.Context;

/// <summary>
/// PWA-side client for the user's organisational memberships
/// (Feature 125, supports ContextChipSwitcher). Wraps
/// <c>GET /api/auth/me/organizations</c>.
/// </summary>
public interface IUserOrgMembershipsClient
{
    /// <summary>List every org the signed-in user holds a membership in.</summary>
    Task<IReadOnlyList<UserOrgMembership>> ListAsync(CancellationToken ct = default);
}

/// <summary>
/// One organisational membership the user holds. Personal context is not
/// represented in this list — the chip switcher always renders Personal as an
/// always-present option on top of the returned memberships.
/// </summary>
public sealed record UserOrgMembership(
    Guid OrganizationId,
    string Name,
    string? Role);

/// <summary>HTTP-backed <see cref="IUserOrgMembershipsClient"/>.</summary>
public sealed class HttpUserOrgMembershipsClient : IUserOrgMembershipsClient
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpUserOrgMembershipsClient> _logger;

    /// <summary>Initialise a new client.</summary>
    public HttpUserOrgMembershipsClient(HttpClient http, ILogger<HttpUserOrgMembershipsClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserOrgMembership>> ListAsync(CancellationToken ct = default)
    {
        try
        {
            var payload = await _http.GetFromJsonAsync<OrgListEnvelope>("/api/auth/me/organizations", JsonDefaults.Api, ct)
                .ConfigureAwait(false);
            return payload?.Organizations?
                .Select(o => new UserOrgMembership(o.OrganizationId, o.Name, o.Role))
                .ToList() ?? new List<UserOrgMembership>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to list org memberships; defaulting to empty list.");
            return Array.Empty<UserOrgMembership>();
        }
    }

    private sealed record OrgListEnvelope(
        [property: JsonPropertyName("organizations")] List<OrgEntry>? Organizations);

    private sealed record OrgEntry(
        [property: JsonPropertyName("organizationId")] Guid OrganizationId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("role")] string? Role);
}

/// <summary>In-memory <see cref="IUserOrgMembershipsClient"/> for unit tests.</summary>
public sealed class InMemoryUserOrgMembershipsClient : IUserOrgMembershipsClient
{
    private readonly IReadOnlyList<UserOrgMembership> _memberships;

    /// <summary>Seed the in-memory client with a static membership list.</summary>
    public InMemoryUserOrgMembershipsClient(IReadOnlyList<UserOrgMembership>? memberships = null)
        => _memberships = memberships ?? Array.Empty<UserOrgMembership>();

    /// <inheritdoc />
    public Task<IReadOnlyList<UserOrgMembership>> ListAsync(CancellationToken ct = default)
        => Task.FromResult(_memberships);
}
