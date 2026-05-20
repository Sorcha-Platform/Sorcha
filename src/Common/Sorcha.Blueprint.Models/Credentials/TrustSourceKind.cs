// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// One pluggable means of vouching for a credential issuer (feature 135). Each kind
/// maps to an <c>ITrustSourceResolver</c> consulted by the unified trust evaluator.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrustSourceKind
{
    /// <summary>Sorcha register / decentralised-identifier resolution, including assertionMethod and alsoKnownAs equivalence.</summary>
    [JsonStringEnumMemberName("register")]
    Register = 0,

    /// <summary>Tenant X.509 certificate authority — chain validation to the tenant root with CRL.</summary>
    [JsonStringEnumMemberName("x509-tenant")]
    X509Tenant = 1,

    /// <summary>External trust list of roots (EU LOTL) loaded into the certificate trust store.</summary>
    [JsonStringEnumMemberName("trustlist")]
    TrustList = 2,

    /// <summary>Explicit allowlist of issuer identifiers, with alsoKnownAs equivalence.</summary>
    [JsonStringEnumMemberName("did-allowlist")]
    DidAllowlist = 3
}
