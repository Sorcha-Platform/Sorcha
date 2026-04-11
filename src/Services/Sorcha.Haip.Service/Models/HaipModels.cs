// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Haip.Service.Models;

/// <summary>
/// Transient credential offer stored in Redis. Created when a Blueprint action
/// triggers HAIP-path issuance; redeemed when the external wallet exchanges
/// the pre-authorized code.
/// </summary>
public class CredentialOffer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string PreAuthorizedCode { get; set; }
    public required string IssuerWalletAddress { get; set; }
    public required string CredentialType { get; set; }
    public required string TenantId { get; set; }
    public Dictionary<string, object> Claims { get; set; } = new();
    public List<string> DisclosablePaths { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.Pending;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OfferStatus
{
    Pending = 0,
    Exchanged = 1,
    Expired = 2,
    Cancelled = 3
}

/// <summary>
/// OAuth 2.0 token request for the pre-authorized code grant.
/// Form-encoded per RFC 6749 §4.1.3. Field names use underscores/hyphens
/// as specified by the OAuth 2.0 and OpenID4VCI specs.
/// </summary>
public class TokenRequest
{
    [JsonPropertyName("grant_type")]
    [Microsoft.AspNetCore.Mvc.FromForm(Name = "grant_type")]
    public string GrantType { get; set; } = string.Empty;

    [JsonPropertyName("pre-authorized_code")]
    [Microsoft.AspNetCore.Mvc.FromForm(Name = "pre-authorized_code")]
    public string PreAuthorizedCode { get; set; } = string.Empty;
}

/// <summary>
/// OAuth 2.0 token response with c_nonce for credential request proof binding.
/// </summary>
public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("c_nonce")]
    public required string CNonce { get; init; }

    [JsonPropertyName("c_nonce_expires_in")]
    public int CNonceExpiresIn { get; init; }
}

/// <summary>
/// Credential request from the wallet, containing the format and JWT proof of possession.
/// </summary>
public class CredentialRequest
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = "vc+sd-jwt";

    [JsonPropertyName("vct")]
    public string? Vct { get; set; }

    [JsonPropertyName("proof")]
    public CredentialRequestProof? Proof { get; set; }
}

/// <summary>
/// JWT proof of possession in a credential request.
/// </summary>
public class CredentialRequestProof
{
    [JsonPropertyName("proof_type")]
    public string ProofType { get; set; } = "jwt";

    [JsonPropertyName("jwt")]
    public string Jwt { get; set; } = string.Empty;
}

/// <summary>
/// OpenID4VCI issuer metadata document per HAIP 1.0 Section 5.
/// </summary>
public class IssuerMetadata
{
    [JsonPropertyName("credential_issuer")]
    public required string CredentialIssuer { get; init; }

    [JsonPropertyName("credential_endpoint")]
    public required string CredentialEndpoint { get; init; }

    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    [JsonPropertyName("nonce_endpoint")]
    public required string NonceEndpoint { get; init; }

    [JsonPropertyName("credentials_supported")]
    public required List<CredentialSupported> CredentialsSupported { get; init; }

    [JsonPropertyName("display")]
    public List<IssuerDisplay>? Display { get; init; }
}

/// <summary>
/// Descriptor for a supported credential type in issuer metadata.
/// </summary>
public class CredentialSupported
{
    [JsonPropertyName("format")]
    public string Format { get; init; } = "vc+sd-jwt";

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("cryptographic_binding_methods_supported")]
    public List<string> CryptographicBindingMethodsSupported { get; init; } = ["jwk"];

    [JsonPropertyName("credential_signing_alg_values_supported")]
    public List<string> CredentialSigningAlgValuesSupported { get; init; } = ["ES256"];

    [JsonPropertyName("display")]
    public List<CredentialDisplay>? Display { get; init; }
}

/// <summary>
/// Display metadata for the issuer.
/// </summary>
public class IssuerDisplay
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = "en";
}

/// <summary>
/// Display metadata for a credential type.
/// </summary>
public class CredentialDisplay
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = "en";
}
