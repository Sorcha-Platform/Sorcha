// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Haip.Service.Models;

/// <summary>
/// Transient credential offer stored in Redis. Created when a Blueprint action
/// triggers HAIP-path issuance; redeemed when the external wallet exchanges
/// the pre-authorized code.
/// </summary>
public class CredentialOffer
{
    /// <summary>Unique identifier for the resource.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>The pre authorized code.</summary>
    public required string PreAuthorizedCode { get; set; }
    /// <summary>The issuer wallet address.</summary>
    public required string IssuerWalletAddress { get; set; }
    /// <summary>The credential type.</summary>
    public required string CredentialType { get; set; }
    /// <summary>Identifier of the tenant scope.</summary>
    public required string TenantId { get; set; }
    /// <summary>Claims included in the credential or token.</summary>
    public Dictionary<string, object> Claims { get; set; } = new();
    /// <summary>Collection of disclosable paths associated with this resource.</summary>
    public List<string> DisclosablePaths { get; set; } = new();
    /// <summary>Server timestamp when the record was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Timestamp at which the record expires (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>Current status of the resource.</summary>
    public OfferStatus Status { get; set; } = OfferStatus.Pending;
    /// <summary>Credential format to mint (feature 135 US3). Default SD-JWT VC.</summary>
    public CredentialFormat Format { get; set; } = CredentialFormat.SdJwtVc;
    /// <summary>Trust anchor the credential is issued under (feature 135 US3). Default register.</summary>
    public TrustAnchor TrustAnchor { get; set; } = TrustAnchor.Register;
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
    /// <summary>The grant type.</summary>
    [JsonPropertyName("grant_type")]
    [Microsoft.AspNetCore.Mvc.FromForm(Name = "grant_type")]
    public string GrantType { get; set; } = string.Empty;

    /// <summary>The pre authorized code.</summary>
    [JsonPropertyName("pre-authorized_code")]
    [Microsoft.AspNetCore.Mvc.FromForm(Name = "pre-authorized_code")]
    public string PreAuthorizedCode { get; set; } = string.Empty;
}

/// <summary>
/// OAuth 2.0 token response with c_nonce for credential request proof binding.
/// </summary>
public class TokenResponse
{
    /// <summary>OAuth access token.</summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>The token type.</summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    /// <summary>Numeric value for expires in.</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    /// <summary>The c nonce.</summary>
    [JsonPropertyName("c_nonce")]
    public required string CNonce { get; init; }

    /// <summary>Numeric value for c nonce expires in.</summary>
    [JsonPropertyName("c_nonce_expires_in")]
    public int CNonceExpiresIn { get; init; }
}

/// <summary>
/// Credential request from the wallet, containing the format and JWT proof of possession.
/// </summary>
public class CredentialRequest
{
    /// <summary>Format identifier for the payload.</summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "dc+sd-jwt";

    /// <summary>The vct.</summary>
    [JsonPropertyName("vct")]
    public string? Vct { get; set; }

    /// <summary>The proof.</summary>
    [JsonPropertyName("proof")]
    public CredentialRequestProof? Proof { get; set; }
}

/// <summary>
/// JWT proof of possession in a credential request.
/// </summary>
public class CredentialRequestProof
{
    /// <summary>The proof type.</summary>
    [JsonPropertyName("proof_type")]
    public string ProofType { get; set; } = "jwt";

    /// <summary>The jwt.</summary>
    [JsonPropertyName("jwt")]
    public string Jwt { get; set; } = string.Empty;
}

/// <summary>
/// OpenID4VCI issuer metadata document per HAIP 1.0 Section 5.
/// </summary>
public class IssuerMetadata
{
    /// <summary>The credential issuer.</summary>
    [JsonPropertyName("credential_issuer")]
    public required string CredentialIssuer { get; init; }

    /// <summary>The credential endpoint.</summary>
    [JsonPropertyName("credential_endpoint")]
    public required string CredentialEndpoint { get; init; }

    /// <summary>The token endpoint.</summary>
    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    /// <summary>The nonce endpoint.</summary>
    [JsonPropertyName("nonce_endpoint")]
    public required string NonceEndpoint { get; init; }

    /// <summary>Collection of credentials supported associated with this resource.</summary>
    [JsonPropertyName("credentials_supported")]
    public required List<CredentialSupported> CredentialsSupported { get; init; }

    /// <summary>Collection of display associated with this resource.</summary>
    [JsonPropertyName("display")]
    public List<IssuerDisplay>? Display { get; init; }
}

/// <summary>
/// Descriptor for a supported credential type in issuer metadata.
/// </summary>
public class CredentialSupported
{
    /// <summary>Format identifier for the payload.</summary>
    [JsonPropertyName("format")]
    public string Format { get; init; } = "dc+sd-jwt";

    /// <summary>Unique identifier for the resource.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Collection of cryptographic binding methods supported associated with this resource.</summary>
    [JsonPropertyName("cryptographic_binding_methods_supported")]
    public List<string> CryptographicBindingMethodsSupported { get; init; } = ["jwk"];

    /// <summary>Collection of credential signing alg values supported associated with this resource.</summary>
    [JsonPropertyName("credential_signing_alg_values_supported")]
    public List<string> CredentialSigningAlgValuesSupported { get; init; } = ["ES256"];

    /// <summary>Collection of display associated with this resource.</summary>
    [JsonPropertyName("display")]
    public List<CredentialDisplay>? Display { get; init; }
}

/// <summary>
/// Display metadata for the issuer.
/// </summary>
public class IssuerDisplay
{
    /// <summary>Human-readable name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The locale.</summary>
    [JsonPropertyName("locale")]
    public string Locale { get; init; } = "en";
}

/// <summary>
/// Display metadata for a credential type.
/// </summary>
public class CredentialDisplay
{
    /// <summary>Human-readable name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The locale.</summary>
    [JsonPropertyName("locale")]
    public string Locale { get; init; } = "en";
}
