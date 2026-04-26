// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Typed wrapper around the JWT payload of a <c>citizen-device-delegation/v1</c>
/// SD-JWT VC. Mirrors the JSON Schema in
/// <c>Schemas/device-delegation-credential.v1.json</c>.
/// </summary>
/// <remarks>
/// This is the verifier-consumable artefact that proves a specific device key
/// has been authorised by the citizen's holder key to make presentations.
/// Issuer = <c>did:sorcha:holder:{thumbprint}</c>; subject = <c>did:sorcha:device:{thumbprint}</c>.
/// </remarks>
public sealed record DeviceDelegationCredential
{
    /// <summary>Issuer DID — the holder identity. Pattern: <c>did:sorcha:holder:{43-char base64url}</c>.</summary>
    [JsonPropertyName("iss")]
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Subject DID — the device identity. Pattern: <c>did:sorcha:device:{43-char base64url}</c>.</summary>
    [JsonPropertyName("sub")]
    public string Subject { get; init; } = string.Empty;

    /// <summary>Issued-at time (seconds since epoch).</summary>
    [JsonPropertyName("iat")]
    public long IssuedAt { get; init; }

    /// <summary>Expiry time (seconds since epoch). Default issuance: iat + 12 months.</summary>
    [JsonPropertyName("exp")]
    public long Expires { get; init; }

    /// <summary>Verifiable Credential Type. MUST equal <see cref="Constants.VctUris.CitizenDeviceDelegationV1"/>.</summary>
    [JsonPropertyName("vct")]
    public string Vct { get; init; } = Constants.VctUris.CitizenDeviceDelegationV1;

    /// <summary>Capabilities granted by the holder to the device.</summary>
    [JsonPropertyName("delegated_capabilities")]
    public IReadOnlyList<string> DelegatedCapabilities { get; init; } =
        new ReadOnlyCollection<string>([Constants.DelegatedCapabilities.PresentationHolderKeyBinding]);

    /// <summary>Device descriptor block.</summary>
    [JsonPropertyName("device")]
    public DeviceDescriptor Device { get; init; } = new();

    /// <summary>Confirmation method — the device's public key.</summary>
    [JsonPropertyName("cnf")]
    public ConfirmationMethod Confirmation { get; init; } = new();

    /// <summary>Status list pointer for revocation lookup.</summary>
    [JsonPropertyName("status")]
    public StatusBlock Status { get; init; } = new();
}

/// <summary>Device metadata embedded in the delegation credential.</summary>
public sealed record DeviceDescriptor
{
    /// <summary>Citizen-visible label.</summary>
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>Free-form platform descriptor.</summary>
    [JsonPropertyName("platform")]
    public string Platform { get; init; } = string.Empty;

    /// <summary>Original enrolment time (seconds since epoch); preserved across renewals.</summary>
    [JsonPropertyName("enrolled_at")]
    public long EnrolledAt { get; init; }
}

/// <summary>SD-JWT VC <c>cnf</c> block carrying the bound public key.</summary>
public sealed record ConfirmationMethod
{
    /// <summary>The device's public JWK.</summary>
    [JsonPropertyName("jwk")]
    public EcP256PublicJwk Jwk { get; init; } = new();
}

/// <summary>SD-JWT VC <c>status</c> block carrying the status list reference.</summary>
public sealed record StatusBlock
{
    /// <summary>Token Status List 2024 reference.</summary>
    [JsonPropertyName("status_list")]
    public StatusListReference StatusList { get; init; } = new();
}

/// <summary>Status list URI + bit position.</summary>
public sealed record StatusListReference
{
    /// <summary>URL of the org-scoped citizen-devices status list.</summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>Bit position within the status list.</summary>
    [JsonPropertyName("idx")]
    public int Index { get; init; }
}
