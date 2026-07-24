// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;

namespace Sorcha.ServiceClients.Configuration;

/// <summary>
/// The services a Sorcha node addresses over HTTP.
/// </summary>
public enum SorchaService
{
    /// <summary>Tenant Service — identity, orgs, auth, invitations.</summary>
    Tenant,

    /// <summary>Wallet Service — keys, signing, credentials.</summary>
    Wallet,

    /// <summary>Register Service — the distributed ledger.</summary>
    Register,

    /// <summary>Blueprint Service — workflow definitions and instances.</summary>
    Blueprint,

    /// <summary>Validator Service — consensus and chain integrity.</summary>
    Validator,

    /// <summary>Peer Service — P2P replication.</summary>
    Peer,

    /// <summary>HAIP Service — OpenID4VCI / OpenID4VP external-wallet surface.</summary>
    Haip,

    /// <summary>API Gateway — the YARP reverse proxy.</summary>
    ApiGateway,
}

/// <summary>
/// Resolves a service's base address from configuration, honouring every key spelling the platform
/// has accumulated — the sibling of <c>SorchaConnectionsExtensions</c>, which does the same job for
/// connection strings.
/// </summary>
/// <remarks>
/// <para>
/// An audit found <b>19 distinct config-key spellings addressing 8 services</b>. Tenant alone had
/// four: <c>ServiceClients:TenantService:Address</c>, <c>ServiceClients:Tenant:BaseAddress</c>,
/// <c>Services:TenantService:BaseAddress</c> and <c>Services:Tenant:Url</c>. Several call sites
/// hand-rolled their own two- or three-key fallback chain inline, and no two chains agreed on the
/// order or on which spellings to include — so which key a deployment had to set depended on which
/// call site happened to resolve it.
/// </para>
/// <para>
/// <b>Every historical spelling is still accepted</b>, deliberately. Deployments in the wild
/// (docker-compose, n1) set <c>ServiceClients__{X}Service__Address</c> and, for the gateway's
/// aggregation views, <c>Services__{X}__Url</c>. Dropping a spelling here would silently unbind a
/// running node's configuration — the resolver exists to end the drift, not to break deployments.
/// New configuration should use <see cref="CanonicalKey"/>.
/// </para>
/// </remarks>
public static class SorchaServiceAddresses
{
    /// <summary>
    /// The canonical configuration key for a service's base address:
    /// <c>ServiceClients:{Service}Service:Address</c> (<c>ServiceClients:ApiGateway:Address</c> for
    /// the gateway). This is the spelling every deployment already sets.
    /// </summary>
    public static string CanonicalKey(SorchaService service) =>
        service == SorchaService.ApiGateway
            ? "ServiceClients:ApiGateway:Address"
            : $"ServiceClients:{service}Service:Address";

    /// <summary>
    /// Every key spelling consulted for a service, in resolution order: the canonical key first,
    /// then the historical variants that existing configuration may still use.
    /// </summary>
    public static IReadOnlyList<string> KeysFor(SorchaService service) =>
    [
        CanonicalKey(service),
        $"ServiceClients:{service}:BaseAddress",
        $"Services:{service}Service:BaseAddress",
        $"Services:{service}:Url",
        $"{service}Service:Endpoint",
    ];

    /// <summary>
    /// Resolves a service's base address from configuration, or <see langword="null"/> when no key
    /// is set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This resolves keys only — it deliberately supplies no default.</b> The caller keeps its
    /// own fallback, because the existing fallbacks are genuinely not interchangeable: call sites
    /// variously defaulted to <c>http://tenant-service</c>, <c>https+http://tenant-service</c> (the
    /// Aspire service-discovery scheme, resolved by the host rather than DNS),
    /// <c>http://tenant-service:8080</c>, or to throwing. Collapsing those into one default would
    /// change runtime behaviour differently under Aspire and under docker-compose — a separate
    /// question from key drift, and not one to settle silently while fixing key drift.
    /// </para>
    /// <para>
    /// So the shared piece is the <i>key cascade</i>: which keys are consulted, and in what order.
    /// That is what had drifted — six call sites addressing the Tenant Service each hand-rolled a
    /// different chain, so which key a deployment had to set depended on which client resolved it.
    /// </para>
    /// </remarks>
    public static string? TryResolve(IConfiguration configuration, SorchaService service)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (var key in KeysFor(service))
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
