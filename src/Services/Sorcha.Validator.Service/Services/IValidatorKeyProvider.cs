// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Resolves and caches the local validator's docket-signing public key (Feature 108).
/// Used by the roster-driven monitoring bootstrap to ask Register.Service for the list
/// of registers whose roster includes this key. Returns <c>null</c> when the validator
/// is not yet initialised (no system wallet).
/// </summary>
public interface IValidatorKeyProvider
{
    /// <summary>
    /// Get the validator's public key (Base64-decoded raw bytes), caching on first access.
    /// Returns <c>null</c> when the system wallet is not yet provisioned.
    /// </summary>
    Task<byte[]?> GetValidatorPublicKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Force the next call to re-resolve.</summary>
    void Invalidate();
}
