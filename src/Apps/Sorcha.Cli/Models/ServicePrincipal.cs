// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Cli.Models;

/// <summary>
/// Represents a service principal (application identity) in the Sorcha platform.
/// </summary>
/// <remarks>
/// Mirrors <c>Sorcha.Tenant.Service.Models.Dtos.ServicePrincipalResponse</c> exactly; the pairing
/// is asserted by <c>CliWireContractTests</c>. This previously declared <c>name</c>,
/// <c>description</c>, <c>isActive</c>, <c>organizationId</c>, <c>updatedAt</c> and
/// <c>secretRotatedAt</c> — none of which the server sends — so `service-principal list` and `get`
/// showed a blank name and an always-true active flag, while the server's real <c>id</c>,
/// <c>serviceName</c> and <c>status</c> were discarded.
/// </remarks>
public class ServicePrincipal
{
    /// <summary>The service principal's unique id.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name for the service principal.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>OAuth2 client id used when authenticating as this principal.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Scopes granted to this service principal.</summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>Current status, e.g. <c>Active</c>.</summary>
    public string Status { get; set; } = "Active";

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Request to create a new service principal.
/// </summary>
public class CreateServicePrincipalRequest
{
    /// <summary>
    /// Display name for the service principal.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the service principal's purpose.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Scopes/permissions to grant.
    /// </summary>
    public List<string>? Scopes { get; set; }
}

/// <summary>
/// Response when creating a service principal (includes the client secret).
/// </summary>
public class CreateServicePrincipalResponse
{
    /// <summary>
    /// The created service principal.
    /// </summary>
    public ServicePrincipal ServicePrincipal { get; set; } = null!;

    /// <summary>
    /// Client secret (only returned on creation, cannot be retrieved later).
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>
/// Response when rotating a service principal secret.
/// </summary>
/// <remarks>
/// Mirrors <c>Sorcha.Tenant.Service.Models.Dtos.RotateSecretResponse</c> exactly; the pairing is
/// asserted by <c>CliWireContractTests</c>. This previously declared <c>ClientSecret</c> and
/// <c>RotatedAt</c>, neither of which the server sends — so the command printed an empty secret
/// and a default timestamp while reporting success. The rotation itself had happened, leaving the
/// operator with a rotated principal and no way to recover the new secret.
/// </remarks>
public class RotateSecretResponse
{
    /// <summary>
    /// The new client secret. Returned once and never again.
    /// </summary>
    public string NewClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Server-supplied warning shown alongside the secret.
    /// </summary>
    public string Warning { get; set; } = string.Empty;
}
