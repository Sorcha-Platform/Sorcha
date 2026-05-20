// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// The anchor an issued credential is trusted under (feature 135). Selected on
/// <see cref="CredentialIssuanceConfig"/> and determines what trust material the
/// issued credential carries.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrustAnchor
{
    /// <summary>Verifiable via Sorcha decentralised-identifier resolution; no certificate chain attached. Default.</summary>
    [JsonStringEnumMemberName("register")]
    Register = 0,

    /// <summary>Trusted under the tenant's X.509 certificate authority; the org leaf-to-root chain is attached.</summary>
    [JsonStringEnumMemberName("x509-tenant")]
    X509Tenant = 1,

    /// <summary>Trusted under an external trust list (EU LOTL) root; the corresponding chain is attached.</summary>
    [JsonStringEnumMemberName("x509-lotl")]
    X509Lotl = 2
}
