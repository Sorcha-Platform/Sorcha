// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.OrgDidDocument;

/// <summary>
/// Wallet → Tenant cross-service client that triggers DID-document regeneration
/// when a wallet-side key event occurs (Feature 120 US2).
/// </summary>
public interface IOrgDidDocumentClient
{
    /// <summary>
    /// Triggers regeneration of the org's published DID document with the supplied
    /// key snapshot. Returns silently on success; logs and swallows on failure
    /// (key derivation is the source of truth — DID-doc regeneration is a derived
    /// projection that can be lazily rebuilt later).
    /// </summary>
    Task RegenerateAsync(OrgDidRegenerateRequest request, CancellationToken ct = default);
}
