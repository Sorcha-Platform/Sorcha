// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Wallet.Service.Models;

/// <summary>
/// Feature 095 US3 — selector for which status-list claim shape a credential
/// carries in its signed SD-JWT payload.
/// </summary>
/// <remarks>
/// The default value is <see cref="W3cBitstringStatusListEntry"/>, which preserves
/// the spec 093 behaviour for any caller that does not opt in. HAIP-path issuance
/// explicitly selects <see cref="IetfTokenStatusList"/> so external wallets and
/// verifiers can read the status via IETF Token Status List semantics.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatusClaimForm
{
    /// <summary>
    /// Embed a W3C <c>credentialStatus</c> claim with a
    /// <c>BitstringStatusListEntry</c>. Spec 093 behaviour.
    /// </summary>
    W3cBitstringStatusListEntry = 0,

    /// <summary>
    /// Embed an IETF <c>status.status_list</c> claim carrying <c>uri</c> and
    /// <c>idx</c>. The issuer publishes the backing bitstring at the
    /// <c>/api/v1/credentials/ietf-status-lists/{listId}</c> endpoint as a
    /// signed JWT with <c>typ=statuslist+jwt</c>.
    /// </summary>
    IetfTokenStatusList = 1,
}
