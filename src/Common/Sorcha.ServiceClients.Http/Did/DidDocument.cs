// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// W3C DID Core document describing a decentralized identifier and its verification methods.
/// </summary>
public class DidDocument
{
    /// <summary>
    /// The DID this document describes.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Public keys and their types used for cryptographic operations.
    /// </summary>
    [JsonPropertyName("verificationMethod")]
    public IReadOnlyList<VerificationMethod> VerificationMethod { get; set; } = [];

    /// <summary>
    /// Key IDs used for authentication.
    /// </summary>
    [JsonPropertyName("authentication")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Authentication { get; set; }

    /// <summary>
    /// Key IDs used for signing credentials and presentations.
    /// </summary>
    [JsonPropertyName("assertionMethod")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AssertionMethod { get; set; }

    /// <summary>
    /// Service endpoints associated with this DID.
    /// </summary>
    [JsonPropertyName("service")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ServiceEndpoint>? Service { get; set; }
}

/// <summary>
/// A public key or verification method within a DID Document.
/// </summary>
public class VerificationMethod
{
    /// <summary>
    /// Key identifier (e.g., "did:example:123#key-1").
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Key type: "JsonWebKey2020", "Ed25519VerificationKey2020", etc.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>
    /// DID of the entity that controls this key.
    /// </summary>
    [JsonPropertyName("controller")]
    public required string Controller { get; set; }

    /// <summary>
    /// JWK representation of the public key.
    /// </summary>
    [JsonPropertyName("publicKeyJwk")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? PublicKeyJwk { get; set; }

    /// <summary>
    /// Multibase-encoded public key.
    /// </summary>
    [JsonPropertyName("publicKeyMultibase")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublicKeyMultibase { get; set; }
}

/// <summary>
/// A service endpoint associated with a DID.
/// </summary>
public class ServiceEndpoint
{
    /// <summary>
    /// Service endpoint identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Service type (e.g., "LinkedDomains").
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>
    /// Service endpoint URL.
    /// </summary>
    [JsonPropertyName("serviceEndpoint")]
    public required string Endpoint { get; set; }
}
