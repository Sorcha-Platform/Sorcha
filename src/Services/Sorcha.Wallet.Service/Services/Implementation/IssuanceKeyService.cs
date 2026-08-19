// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Cryptography;
using Sorcha.ServiceClients.OrgDidDocument;
using Sorcha.ServiceClients.OrgInfo;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Lazy-derivation entry point for per-organisation VC issuance keys (Feature 120 T038).
/// </summary>
/// <remarks>
/// On first call for an org, derives a key under <see cref="KeyUsage.VCIssuance"/>
/// (Feature 083 slot 1) via the existing <see cref="IOrgKeyDerivationService"/>,
/// persists an <see cref="IssuanceKeyState"/> row with <c>Status=Active, RotationIndex=1</c>,
/// and triggers DID-document regeneration on the Tenant side. Idempotent on retries —
/// returns the existing Active row.
/// </remarks>
public sealed class IssuanceKeyService : IIssuanceKeyService
{
    private const int IssuanceSlot = 1; // Feature 083 slot 1 = KeyUsage.VCIssuance

    private readonly WalletDbContext _db;
    private readonly IOrgKeyDerivationService _orgKey;
    private readonly IOrgDidDocumentClient _didDocClient;
    private readonly IOrgInfoClient _orgInfo;
    private readonly Sorcha.Wallet.Core.Services.Interfaces.IOrgKeyProtectionProvider _orgKeyProtection;
    private readonly ILogger<IssuanceKeyService> _logger;

    /// <summary>DI-friendly constructor.</summary>
    public IssuanceKeyService(
        WalletDbContext db,
        IOrgKeyDerivationService orgKey,
        IOrgDidDocumentClient didDocClient,
        IOrgInfoClient orgInfo,
        Sorcha.Wallet.Core.Services.Interfaces.IOrgKeyProtectionProvider orgKeyProtection,
        ILogger<IssuanceKeyService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _orgKey = orgKey ?? throw new ArgumentNullException(nameof(orgKey));
        _didDocClient = didDocClient ?? throw new ArgumentNullException(nameof(didDocClient));
        _orgInfo = orgInfo ?? throw new ArgumentNullException(nameof(orgInfo));
        _orgKeyProtection = orgKeyProtection ?? throw new ArgumentNullException(nameof(orgKeyProtection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IssuanceKeyState?> GetOrDeriveAsync(Guid organizationId, CancellationToken ct = default)
    {
        // Idempotency — return any existing Active row first.
        var existing = await _db.IssuanceKeyStates
            .FirstOrDefaultAsync(
                k => k.OrganizationId == organizationId
                  && k.Slot == IssuanceSlot
                  && k.Status == IssuanceKeyStatus.Active,
                ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            // The key exists, but that says nothing about whether its DID document was ever
            // published. The eager publish below races Tenant's OrgWalletReconciliationService,
            // which provisions the org's canonical wallet asynchronously — for a brand-new org the
            // derive wins that race by a few seconds, the publish is skipped, and this early return
            // meant nothing ever re-attempted it (issue #1518).
            //
            // Issuance itself was never at risk: GetActiveSigningMaterialAsync re-ensures before
            // every signature and fails closed. But that leaves did.json 404 for the whole window
            // between deriving a key and first signing with it, so an org can advertise an issuance
            // key whose issuer DID does not resolve — and any consumer reading the document before
            // the org has issued anything gets a 404.
            //
            // Best-effort and idempotent: the Tenant side no-ops on an unchanged key-version
            // fingerprint, so this is one round trip, and a failure here must not fail the lookup.
            await EnsureDidDocumentPublishedAsync(
                organizationId, "IssuanceKeyEnsured", canonicalAddress: null, ct).ConfigureAwait(false);

            return existing;
        }

        // Derive via Feature 083 — uses orgId as both controller and subject for the org's own key.
        // OrgKeyDerivationService throws InvalidOperationException when the org has no
        // provisioned master key. Treat that as 'F120 lazy derivation not yet applicable'
        // and return null rather than blowing up the credential-mint call site — master keys
        // are an explicit org-setup step, not something we want to silently provision here
        // (provisioning generates a recovery mnemonic that must be backed up).
        DerivedKeyResult derived;
        try
        {
            derived = await _orgKey.DeriveUserKeyAsync(
                organizationId.ToString(),
                organizationId.ToString(),
                departmentId: 0,
                usage: KeyUsage.VCIssuance,
                ct: ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No active master key", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Issuance key derivation skipped for org {OrgId}: no provisioned master key — Feature 083 setup not yet run for this org.",
                organizationId);
            return null;
        }

        // Look up the persisted wallet to read the public key bytes.
        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.Address == derived.WalletAddress, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Issuance key derivation produced wallet address '{derived.WalletAddress}' " +
                "but the wallet row could not be found. Underlying derivation pipeline failure.");

        var publicKeyBytes = string.IsNullOrEmpty(wallet.PublicKey)
            ? throw new InvalidOperationException("Derived wallet has no public key recorded.")
            : Convert.FromBase64String(wallet.PublicKey);

        var jwkJson = BuildJwk(wallet.Algorithm, publicKeyBytes);
        var thumbprint = JsonWebKeyThumbprint.Compute(jwkJson);

        var state = new IssuanceKeyState
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Slot = IssuanceSlot,
            RotationIndex = 1,
            Status = IssuanceKeyStatus.Active,
            PublicKey = publicKeyBytes,
            Algorithm = wallet.Algorithm,
            Thumbprint = thumbprint,
            DerivedAt = DateTimeOffset.UtcNow
        };

        _db.IssuanceKeyStates.Add(state);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Issuance key derived for org {OrgId} algo={Algorithm} rotation={Rotation}",
            organizationId, state.Algorithm, state.RotationIndex);

        // Feature 149: publish the DID document anchored on the CANONICAL operational wallet (A),
        // not the derived child (C). Best-effort here — a failure is NOT fatal to derivation,
        // because GetActiveSigningMaterialAsync re-ensures publication before every signature
        // and fails closed there. That is what makes a failed publish recoverable rather than
        // permanent; this call just gets the common case done early.
        await EnsureDidDocumentPublishedAsync(
            organizationId, "IssuanceKeyDerived", canonicalAddress: null, ct).ConfigureAwait(false);

        return state;
    }

    /// <inheritdoc />
    public Task<bool> PublishDidDocumentAsync(Guid organizationId, CancellationToken ct = default)
        => EnsureDidDocumentPublishedAsync(
            organizationId, "IssuanceKeyEnsured", canonicalAddress: null, ct);

    /// <inheritdoc />
    public Task<IssuanceKeyState?> GetActiveAsync(Guid organizationId, CancellationToken ct = default)
        => _db.IssuanceKeyStates
            .FirstOrDefaultAsync(
                k => k.OrganizationId == organizationId
                  && k.Slot == IssuanceSlot
                  && k.Status == IssuanceKeyStatus.Active,
                ct);

    /// <inheritdoc />
    public async Task<JsonElement?> GetPublicJwkAsync(
        Guid organizationId, int rotationIndex, CancellationToken ct = default)
    {
        var row = await _db.IssuanceKeyStates
            .FirstOrDefaultAsync(
                k => k.OrganizationId == organizationId
                  && k.RotationIndex == rotationIndex,
                ct)
            .ConfigureAwait(false);

        if (row is null) return null;

        var jwkJson = BuildJwk(row.Algorithm, row.PublicKey);
        using var doc = JsonDocument.Parse(jwkJson);
        return doc.RootElement.Clone();
    }

    /// <inheritdoc />
    public async Task<Sorcha.Wallet.Service.Services.Interfaces.IssuanceSigningMaterial?> GetActiveSigningMaterialAsync(
        Guid organizationId, CancellationToken ct = default)
    {
        var state = await _db.IssuanceKeyStates
            .FirstOrDefaultAsync(
                k => k.OrganizationId == organizationId
                  && k.Slot == IssuanceSlot
                  && k.Status == IssuanceKeyStatus.Active,
                ct)
            .ConfigureAwait(false);
        if (state is null) return null;

        // The issuance key's private material lives in the Wallet that
        // OrgKeyDerivationService created when the key was derived. Look it up via
        // the DerivedKeyRecord (KeyUsage = VCIssuance) for this org.
        var derivedRecord = await _db.DerivedKeyRecords
            .FirstOrDefaultAsync(
                d => d.OrganizationId == organizationId.ToString()
                  && d.KeyUsage == KeyUsage.VCIssuance
                  && d.Status == DerivedKeyStatus.Active,
                ct)
            .ConfigureAwait(false);
        if (derivedRecord is null)
        {
            _logger.LogWarning(
                "IssuanceKeyState row exists for org {OrgId} but no matching DerivedKeyRecord was found — schema drift",
                organizationId);
            return null;
        }

        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.Address == derivedRecord.WalletAddress, ct)
            .ConfigureAwait(false);
        if (wallet is null || string.IsNullOrEmpty(wallet.EncryptedPrivateKey))
        {
            _logger.LogWarning(
                "DerivedKeyRecord points at wallet {Addr} but the wallet has no encrypted private key — recovery state",
                derivedRecord.WalletAddress);
            return null;
        }

        byte[] privateKey;
        try
        {
            // Org-derived wallets are encrypted via IOrgKeyProtectionProvider (uses
            // OrgKeyProtection:EncryptionKey) NOT IKeyManagementService (uses the
            // wallet-master-key from LinuxSecretService). Mismatching providers gives
            // an AES-GCM AuthenticationTagMismatchException at runtime.
            privateKey = await _orgKeyProtection
                .DecryptSeedAsync(
                    Convert.FromBase64String(wallet.EncryptedPrivateKey),
                    wallet.EncryptionKeyId,
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to decrypt issuance private key for org {OrgId} wallet {Addr}",
                organizationId, derivedRecord.WalletAddress);
            return null;
        }

        // Feature 149: anchor the issuer DID on the org's CANONICAL operational wallet (A),
        // not the derived VC-issuance child (C). The signing key stays C; verifiers resolve
        // C's public key from the published did:sorcha:org:{A} document (under #vc-issuance-{n}).
        // No resolvable A → fail closed (return null) rather than mint an unverifiable credential.
        var canonicalAddress = await _orgInfo
            .ResolveCanonicalWalletAddressAsync(organizationId, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(canonicalAddress))
        {
            _logger.LogWarning(
                "No canonical operational wallet address for org {OrgId} — cannot anchor a verifiable " +
                "issuer DID; refusing to supply signing material (fail closed).",
                organizationId);
            Array.Clear(privateKey); // drop the decrypted key we will not use
            return null;
        }

        // THE REPAIR PATH. Publication used to fire only on first key derivation, and the
        // client swallowed failures, so a document that never published stayed missing forever
        // while issuance carried on minting credentials no verifier could resolve. Ensuring it
        // here — the last gate before signing — makes every mint self-healing and fails closed
        // when the document backing this kid cannot be published or confirmed.
        //
        // No new availability coupling: resolving canonicalAddress above already requires Tenant.
        if (!await EnsureDidDocumentPublishedAsync(
                organizationId, "IssuanceKeyDerived", canonicalAddress, ct).ConfigureAwait(false))
        {
            _logger.LogError(
                "Refusing to supply issuance signing material for org {OrgId}: the issuer DID "
                + "document could not be published or confirmed, so a credential signed with "
                + "kid #vc-issuance-{Rotation} would be unverifiable.",
                organizationId, state.RotationIndex);
            Array.Clear(privateKey); // drop the decrypted key we will not use
            return null;
        }

        var issuerDid = $"did:sorcha:org:{canonicalAddress}";
        var kid = $"{issuerDid}#vc-issuance-{state.RotationIndex}";

        return new Sorcha.Wallet.Service.Services.Interfaces.IssuanceSigningMaterial(
            OrganizationId: organizationId,
            IssuerDid: issuerDid,
            Kid: kid,
            PrivateKey: privateKey,
            Algorithm: state.Algorithm,
            RotationIndex: state.RotationIndex);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IssuanceKeyState>> ListAllAsync(
        Guid organizationId, CancellationToken ct = default)
    {
        return await _db.IssuanceKeyStates
            .Where(k => k.OrganizationId == organizationId && k.Slot == IssuanceSlot)
            .OrderBy(k => k.RotationIndex)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IssuanceKeyState?> RotateAsync(
        Guid organizationId, Guid governanceOpId, CancellationToken ct = default)
    {
        var existing = await _db.IssuanceKeyStates
            .FirstOrDefaultAsync(
                k => k.OrganizationId == organizationId
                  && k.Slot == IssuanceSlot
                  && k.Status == IssuanceKeyStatus.Active,
                ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _logger.LogWarning(
                "Cannot rotate issuance key for org {OrgId}: no Active row exists. Did you mean to call GetOrDeriveAsync first?",
                organizationId);
            return null;
        }

        // Move existing Active → Rotated.
        existing.Status = IssuanceKeyStatus.Rotated;
        existing.RotatedAt = DateTimeOffset.UtcNow;

        // V1 rotation is a kid-bump only — Feature 083's OrgKeyDerivationService
        // hardcodes KeyIndex=0 and re-derives the same key on every call, so the
        // public key bytes don't change between rotation indices. The new IssuanceKeyState
        // row inherits PublicKey + Thumbprint from the previous Active row; what changes is
        // the kid suffix (#vc-issuance-{newIndex}). Real cryptographic rotation
        // (different key bytes per index) lands when OrgKeyDerivationService gains an
        // explicit rotation-index parameter — separate F083 follow-up.
        var newRow = new IssuanceKeyState
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Slot = IssuanceSlot,
            RotationIndex = existing.RotationIndex + 1,
            Status = IssuanceKeyStatus.Active,
            PublicKey = existing.PublicKey,
            Algorithm = existing.Algorithm,
            Thumbprint = existing.Thumbprint,
            DerivedAt = DateTimeOffset.UtcNow
        };
        _db.IssuanceKeyStates.Add(newRow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Issuance key rotated for org {OrgId}: {OldIdx} → {NewIdx} (governance op {GovOp})",
            organizationId, existing.RotationIndex, newRow.RotationIndex, governanceOpId);

        // DID document regeneration triggered with the new active-keys snapshot.
        // Look up the wallet address from any DerivedKeyRecord — the address doesn't
        // change between rotations under the v1 same-key model.
        var derivedRecord = await _db.DerivedKeyRecords
            .FirstOrDefaultAsync(
                d => d.OrganizationId == organizationId.ToString()
                  && d.KeyUsage == KeyUsage.VCIssuance,
                ct)
            .ConfigureAwait(false);
        if (derivedRecord is not null)
        {
            await EnsureDidDocumentPublishedAsync(
                organizationId, "IssuanceKeyRotated", canonicalAddress: null, ct)
                .ConfigureAwait(false);
        }

        return newRow;
    }

    /// <inheritdoc />
    public async Task<IssuanceKeyState?> RevokeAsync(
        Guid organizationId,
        int rotationIndex,
        string reason,
        Guid governanceOpId,
        CancellationToken ct = default)
    {
        var row = await _db.IssuanceKeyStates
            .FirstOrDefaultAsync(
                k => k.OrganizationId == organizationId
                  && k.Slot == IssuanceSlot
                  && k.RotationIndex == rotationIndex,
                ct)
            .ConfigureAwait(false);

        if (row is null) return null;

        if (row.Status == IssuanceKeyStatus.Revoked)
        {
            _logger.LogDebug(
                "Issuance key already revoked for org {OrgId} rotation {Idx} — idempotent no-op",
                organizationId, rotationIndex);
            return row;
        }

        row.Status = IssuanceKeyStatus.Revoked;
        row.RevokedAt = DateTimeOffset.UtcNow;
        row.RevocationReason = reason;
        row.RevokedByGovernanceOpId = governanceOpId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogWarning(
            "Issuance key revoked for org {OrgId} rotation {Idx} reason='{Reason}' (governance op {GovOp})",
            organizationId, rotationIndex, reason, governanceOpId);

        // Trigger DID document regeneration so the published doc drops the revoked VM
        // from assertionMethod (verifier-side rejection follows from the W3C doc shape,
        // no additional check needed in DidResolverBackedIssuerKeyResolver).
        var derivedRecord = await _db.DerivedKeyRecords
            .FirstOrDefaultAsync(
                d => d.OrganizationId == organizationId.ToString()
                  && d.KeyUsage == KeyUsage.VCIssuance
                  && d.Status == DerivedKeyStatus.Active,
                ct)
            .ConfigureAwait(false);
        if (derivedRecord is not null)
        {
            await EnsureDidDocumentPublishedAsync(
                organizationId, "IssuanceKeyRevoked", canonicalAddress: null, ct)
                .ConfigureAwait(false);
        }

        return row;
    }

    /// <summary>
    /// Builds an active-keys snapshot from the current persisted state, publishes it to the
    /// Tenant Service, and reports whether the org now has a correctly-anchored published DID
    /// document. Idempotent — the Tenant side no-ops on an unchanged key-version fingerprint,
    /// so this is safe to call on every issuance and is the org's only repair path.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the snapshot was accepted, or when the publish failed but a document
    /// anchored on the expected canonical wallet is already published and serving (a transient
    /// Tenant write failure must not block issuance). <c>false</c> otherwise — callers about to
    /// sign MUST fail closed.
    /// </returns>
    private async Task<bool> EnsureDidDocumentPublishedAsync(
        Guid organizationId,
        string keyEventReason,
        string? canonicalAddress,
        CancellationToken ct)
    {
        // Feature 149: anchor the regenerated document on the canonical operational wallet (A).
        canonicalAddress ??= await _orgInfo
            .ResolveCanonicalWalletAddressAsync(organizationId, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(canonicalAddress))
        {
            _logger.LogWarning(
                "Cannot publish DID document for org {OrgId} ({Reason}) — no canonical wallet address.",
                organizationId, keyEventReason);
            return false;
        }

        var activeKeys = await _db.IssuanceKeyStates
            .Where(k => k.OrganizationId == organizationId
                     && k.Slot == IssuanceSlot
                     && k.Status == IssuanceKeyStatus.Active)
            .OrderBy(k => k.RotationIndex)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var snapshotKeys = activeKeys
            .Select(k => new OrgDidActiveKey(
                RotationIndex: k.RotationIndex,
                Algorithm: k.Algorithm,
                PublicKeyJwk: BuildJwk(k.Algorithm, k.PublicKey),
                Thumbprint: k.Thumbprint))
            .ToList();

        var snapshot = new OrgDidRegenerateRequest(
            OrganizationId: organizationId,
            KeyEventReason: keyEventReason,
            WalletAddress: canonicalAddress,
            ActiveKeys: snapshotKeys);

        if (await _didDocClient.RegenerateAsync(snapshot, ct).ConfigureAwait(false))
            return true;

        // The publish failed. Before failing closed, check whether a correctly-anchored
        // document is ALREADY published — a transient Tenant write failure must not block
        // issuance for an org whose document is present and serving.
        var publishedDid = await _didDocClient
            .ResolveCanonicalDidAsync(organizationId, ct)
            .ConfigureAwait(false);
        var expectedDid = $"did:sorcha:org:{canonicalAddress}";

        if (string.Equals(publishedDid, expectedDid, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "DID document publish failed for org {OrgId} ({Reason}), but the already-published "
                + "document is correctly anchored on {ExpectedDid} — continuing.",
                organizationId, keyEventReason, expectedDid);
            return true;
        }

        // Either nothing is published, or what IS published is anchored elsewhere and therefore
        // does not back the kid we are about to sign with.
        _logger.LogError(
            "DID document publish failed for org {OrgId} ({Reason}) and no correctly-anchored "
            + "document is published (expected {ExpectedDid}, found {PublishedDid}). There is no "
            + "background rebuild — issuance must fail closed until this succeeds.",
            organizationId, keyEventReason, expectedDid, publishedDid ?? "(none)");
        return false;
    }

    /// <summary>
    /// Builds the JWK JSON for an issuance public key. Currently supports the wallet
    /// algorithms exposed by <see cref="IOrgKeyDerivationService"/> — ED25519 (OKP),
    /// NIST-P256 (EC), RSA-4096 (RSA). Unknown algorithms raise.
    /// </summary>
    internal static string BuildJwk(string algorithm, byte[] publicKeyBytes)
    {
        var algo = algorithm.ToUpperInvariant();
        return algo switch
        {
            "ED25519" => $$"""{"kty":"OKP","crv":"Ed25519","x":"{{Base64Url(publicKeyBytes)}}"}""",
            "NIST-P256" or "NISTP256" or "P-256" or "P256" or "ECDSA-P256"
                => BuildEcJwk(publicKeyBytes),
            _ => throw new NotSupportedException($"Issuance JWK build unsupported for algorithm '{algorithm}'.")
        };
    }

    private static string BuildEcJwk(byte[] publicKeyBytes)
    {
        // Uncompressed P-256 point: 0x04 || X(32) || Y(32).
        if (publicKeyBytes.Length == 65 && publicKeyBytes[0] == 0x04)
        {
            var x = publicKeyBytes.AsSpan(1, 32);
            var y = publicKeyBytes.AsSpan(33, 32);
            return $$"""{"kty":"EC","crv":"P-256","x":"{{Base64Url(x.ToArray())}}","y":"{{Base64Url(y.ToArray())}}"}""";
        }

        throw new NotSupportedException(
            $"Unsupported P-256 public key encoding (length={publicKeyBytes.Length}, first byte=0x{publicKeyBytes.FirstOrDefault():X2}).");
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
