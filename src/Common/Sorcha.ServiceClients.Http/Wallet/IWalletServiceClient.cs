// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.ServiceClients.Wallet;

/// <summary>
/// Unified client interface for Wallet Service operations
/// </summary>
/// <remarks>
/// This interface combines all Wallet Service operations needed across all consuming services:
/// - Validator Service: System wallet management, signing, verification
/// - Blueprint Service: Encryption/decryption, transaction signing, wallet queries
/// - CLI: Wallet creation, management, queries
///
/// All methods use gRPC when available, falling back to HTTP REST endpoints.
/// </remarks>
public interface IWalletServiceClient
{
    // =========================================================================
    // System Wallet Operations (Validator Service)
    // =========================================================================

    /// <summary>
    /// Creates or retrieves the system wallet for a validator instance
    /// </summary>
    /// <param name="validatorId">Unique validator identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>System wallet ID</returns>
    /// <remarks>
    /// Used by Validator Service to maintain a persistent identity wallet.
    /// Creates a new wallet on first call, returns existing wallet on subsequent calls.
    /// </remarks>
    Task<string> CreateOrRetrieveSystemWalletAsync(
        string validatorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers (imports) a system wallet for a validator from a supplied BIP39 mnemonic.
    /// </summary>
    /// <param name="validatorId">Validator ID — must match the running Validator Service's <c>Validator__ValidatorId</c> config.</param>
    /// <param name="mnemonic">Space-separated BIP39 mnemonic words from the genesis ceremony's validator key file.</param>
    /// <param name="algorithm">Signing algorithm. Defaults to ED25519 (matches the docket-signing default).</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>System wallet address.</returns>
    /// <remarks>
    /// Used by <c>sorcha system-register import-validator-key</c> to seat the genesis-ceremony
    /// validator on a node. Returns 409 Conflict if a system wallet already exists for this
    /// validator id — overwriting silently would invalidate every register whose roster contains
    /// the existing pubkey.
    /// </remarks>
    Task<string> RecoverSystemWalletAsync(
        string validatorId,
        string mnemonic,
        string algorithm = "ED25519",
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Signing Operations (Validator Service, Blueprint Service)
    // =========================================================================

    /// <summary>
    /// Signs data using a wallet's private key
    /// </summary>
    /// <param name="walletId">Wallet ID or address</param>
    /// <param name="dataToSign">Data to sign (hex string)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Signing result with signature, public key, and algorithm</returns>
    Task<WalletSignResult> SignDataAsync(
        string walletId,
        string dataToSign,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs transaction data with a wallet's private key
    /// </summary>
    /// <param name="walletAddress">Wallet address to use for signing</param>
    /// <param name="transactionData">Transaction data to sign</param>
    /// <param name="derivationPath">Optional key derivation path (BIP44 or Sorcha system path like "sorcha:register-attestation")</param>
    /// <param name="isPreHashed">When true, transactionData contains a pre-computed hash that should be signed directly without additional hashing</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Signing result with signature, public key, and algorithm</returns>
    Task<WalletSignResult> SignTransactionAsync(
        string walletAddress,
        byte[] transactionData,
        string? derivationPath = null,
        bool isPreHashed = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Feature 181 US4 — resolve an org's P-256 certificate-issuing public key (SPKI) + eligibility for
    /// the external X.509 rail. Never returns the private key.
    /// </summary>
    /// <param name="walletAddress">Org wallet address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Eligibility, bound-key source, and the DER SubjectPublicKeyInfo when eligible.</returns>
    Task<OrgIssuerCertKeyResolution> ResolveIssuerCertKeyAsync(
        string walletAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Feature 181 US4 — sign a pre-computed digest with the org's P-256 certificate-issuing key.
    /// Returns the raw IEEE P1363 <c>r‖s</c> signature; the caller DER-encodes it for a CSR/certificate.
    /// </summary>
    /// <param name="walletAddress">Org wallet address.</param>
    /// <param name="digest">Pre-computed digest bytes to sign.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Raw r‖s signature bytes.</returns>
    Task<byte[]> SignIssuerCertPreHashedAsync(
        string walletAddress,
        byte[] digest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a digital signature
    /// </summary>
    /// <param name="publicKey">Public key or wallet address</param>
    /// <param name="data">Original data that was signed</param>
    /// <param name="signature">Signature to verify (base64-encoded)</param>
    /// <param name="algorithm">Signature algorithm (ED25519, NIST-P256, RSA-4096)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if signature is valid</returns>
    Task<bool> VerifySignatureAsync(
        string publicKey,
        string data,
        string signature,
        string algorithm,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Encryption Operations (Blueprint Service)
    // =========================================================================

    /// <summary>
    /// Encrypts a payload for a recipient wallet
    /// </summary>
    /// <param name="recipientWalletAddress">Wallet address that will decrypt the payload</param>
    /// <param name="payload">Data to encrypt</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Encrypted payload</returns>
    Task<byte[]> EncryptPayloadAsync(
        string recipientWalletAddress,
        byte[] payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts a payload using a wallet's private key
    /// </summary>
    /// <param name="walletAddress">Wallet address to use for decryption</param>
    /// <param name="encryptedPayload">Encrypted payload to decrypt</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Decrypted payload</returns>
    Task<byte[]> DecryptPayloadAsync(
        string walletAddress,
        byte[] encryptedPayload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts a payload using delegated access authorization
    /// </summary>
    /// <param name="walletAddress">Wallet address to use for decryption</param>
    /// <param name="encryptedPayload">Encrypted payload to decrypt</param>
    /// <param name="delegationToken">Credential token from STS granting delegated decrypt access</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Decrypted payload</returns>
    /// <remarks>
    /// Used when a service needs to decrypt on behalf of a user.
    /// The delegation token grants temporary decrypt permission.
    /// </remarks>
    Task<byte[]> DecryptWithDelegationAsync(
        string walletAddress,
        byte[] encryptedPayload,
        string delegationToken,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Credential Operations (Blueprint Service)
    // =========================================================================

    /// <summary>
    /// Issues a new SD-JWT VC credential using the wallet's signing key
    /// </summary>
    /// <param name="issuerWalletAddress">Wallet address of the issuing authority</param>
    /// <param name="credentialType">Credential type (e.g., "LicenseCredential")</param>
    /// <param name="claims">Credential claims to include</param>
    /// <param name="recipientWallet">Wallet address of the credential recipient</param>
    /// <param name="expiryDuration">Optional ISO 8601 expiry duration (e.g., "P365D")</param>
    /// <param name="disclosableClaims">Optional list of claims supporting selective disclosure</param>
    /// <param name="issuanceBlueprintId">Optional blueprint ID that triggered issuance</param>
    /// <param name="statusListUrl">Optional pre-allocated status list URL. When provided together with
    /// <paramref name="statusListIndex"/>, the issuer embeds a W3C BitstringStatusListEntry
    /// credentialStatus claim in the signed SD-JWT payload (Feature 093 US2).</param>
    /// <param name="statusListIndex">Optional pre-allocated status list index. See <paramref name="statusListUrl"/>.</param>
    /// <param name="statusListPurpose">Optional status purpose identifier. Defaults to "revocation" when the pair is provided.</param>
    /// <param name="holderJwk">Feature 137 — recipient holder's public JWK (slot 108) for the SD-JWT
    /// <c>cnf</c> key-confirmation binding. When supplied, the issued credential is bound to the holder
    /// key so only the holder can present it. Null leaves the credential unbound (pre-137 behaviour).</param>
    /// <param name="trustAnchor">Feature 181 US4 — the credential's X.509 trust anchor selector.</param>
    /// <param name="vct">Canonical SD-JWT VC type identifier (the <c>vct</c> claim). MUST be an absolute
    /// URI, case-sensitive exact match per SD-JWT VC §3.2.2.1. When null, <paramref name="credentialType"/>
    /// is used as the vct (defensive fallback for legacy callers). Decoupling the vct from the bare
    /// credential type is what lets the citizen's stored credential carry the canonical URI.</param>
    /// <param name="displayName">Authored human-readable credential name shown on the wallet card
    /// (e.g. "Assured Identity"), decoupled from the vct URI. When supplied, the issuer records it as
    /// <c>credentialName</c> in the credential's display config so the citizen card never depends on
    /// parsing the URI.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Issued credential information including the signed SD-JWT VC token</returns>
    Task<CredentialIssuanceResult> IssueCredentialAsync(
        string issuerWalletAddress,
        string credentialType,
        Dictionary<string, object> claims,
        string recipientWallet,
        string? expiryDuration = null,
        List<string>? disclosableClaims = null,
        string? issuanceBlueprintId = null,
        string? statusListUrl = null,
        int? statusListIndex = null,
        string? statusListPurpose = null,
        bool skipRecipientStore = false,
        string? issuerOrgName = null,
        string? tenantId = null,
        JsonElement? holderJwk = null,
        string? trustAnchor = null,
        string? vct = null,
        string? displayName = null,
        // Register the credential is issued against. Persisted on the ISSUER's row so the
        // credential-lifecycle endpoints can post a CredentialStatusChange when it is later
        // revoked — without it that ledger write is skipped silently (#1482).
        string? registerId = null,
        // The issuer's SUSPENSION list. W3C makes revocation irreversible and suspension
        // reversible, so a credential needs an entry per purpose — sharing one bit reports a
        // suspended credential as revoked.
        string? suspensionStatusListUrl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a credential by ID from a wallet.
    /// </summary>
    /// <returns>
    /// The credential, or <c>null</c> ONLY when the wallet genuinely holds no credential with
    /// that id (HTTP 404).
    /// </returns>
    /// <remarks>
    /// Throws on any other failure — a non-404 status, or a response that cannot be mapped from
    /// the wallet's stored-credential shape. Callers must not treat those as absence: doing so is
    /// what made issue #1475 present a deserialisation mismatch as a bodiless "credential not
    /// found", silently disabling revoke / suspend / reinstate / refresh platform-wide.
    /// </remarks>
    Task<CredentialIssuanceResult?> GetCredentialAsync(
        string walletAddress,
        string credentialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the status of a credential in a wallet (e.g., "Active" → "Revoked")
    /// </summary>
    Task<bool> UpdateCredentialStatusAsync(
        string walletAddress,
        string credentialId,
        string status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a pre-issued credential in a wallet
    /// </summary>
    /// <param name="walletAddress">Wallet address to store the credential in</param>
    /// <param name="credentialId">Unique credential identifier</param>
    /// <param name="type">Credential type</param>
    /// <param name="issuerDid">Issuer DID or wallet address</param>
    /// <param name="subjectDid">Subject DID or wallet address</param>
    /// <param name="claimsJson">Claims as JSON string</param>
    /// <param name="issuedAt">When the credential was issued</param>
    /// <param name="expiresAt">When the credential expires (null for no expiry)</param>
    /// <param name="rawToken">The complete SD-JWT VC token</param>
    /// <param name="issuanceTxId">Optional ledger transaction ID</param>
    /// <param name="issuanceBlueprintId">Optional blueprint ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StoreCredentialAsync(
        string walletAddress,
        string credentialId,
        string type,
        string issuerDid,
        string subjectDid,
        string claimsJson,
        DateTimeOffset issuedAt,
        DateTimeOffset? expiresAt,
        string rawToken,
        string? issuanceTxId = null,
        string? issuanceBlueprintId = null,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // File Download (Feature 085 — Stored Data Transactions)
    // =========================================================================

    /// <summary>
    /// Downloads a decrypted file attachment from a sealed action transaction.
    /// The Wallet Service fetches chunks, decrypts, reassembles, and streams the file.
    /// </summary>
    /// <param name="walletAddress">Wallet address of the requesting participant</param>
    /// <param name="registerId">Register containing the action and chunk transactions</param>
    /// <param name="txId">Transaction ID of the parent action</param>
    /// <param name="fieldName">JSON field name in the action payload</param>
    /// <param name="fileIndex">Index within an array file field (0 for single file fields)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>File download stream result, or null if not found</returns>
    Task<FileDownloadStreamResult?> DownloadFileAsync(
        string walletAddress,
        string registerId,
        string txId,
        string fieldName,
        int fileIndex = 0,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Wallet Management (All Services)
    // =========================================================================

    /// <summary>
    /// Gets wallet information by address
    /// </summary>
    /// <param name="walletAddress">Wallet address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Wallet information, or null if not found</returns>
    Task<WalletInfo?> GetWalletAsync(
        string walletAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists wallets owned by a specific user (service-to-service lookup).
    /// </summary>
    /// <remarks>
    /// Resolves the caller's user id (the <c>Owner</c> field on wallets,
    /// populated from <c>NameIdentifier</c> / <c>sub</c> on wallet creation)
    /// without requiring the caller to hold a <c>wallet_address</c> JWT claim.
    /// Used by the Blueprint Service's pending-actions query so that a
    /// consumer whose token was issued before they created their first wallet
    /// still sees their pending actions after the wallet exists. Calls
    /// <c>GET /api/v1/wallets/by-owner/{ownerId}</c> and requires a service
    /// principal token — the endpoint enforces the <c>RequireService</c>
    /// authorisation policy.
    /// </remarks>
    /// <param name="ownerId">User id (the wallet's <c>Owner</c> value).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Zero or more wallets belonging to the owner.</returns>
    Task<IReadOnlyList<WalletInfo>> GetWalletsByOwnerAsync(
        string ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new wallet
    /// </summary>
    /// <param name="name">Wallet name</param>
    /// <param name="algorithm">Cryptographic algorithm (ED25519, NIST-P256, RSA-4096)</param>
    /// <param name="owner">Owner principal</param>
    /// <param name="tenant">Tenant ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created wallet information</returns>
    Task<WalletInfo> CreateWalletAsync(
        string name,
        string algorithm,
        string owner,
        string tenant,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a wallet owned by a PLATFORM organisation and returns its recovery phrase (#1530).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberate, narrow exception to #1525. Organisation wallets are normally created by an
    /// administrator OF that organisation, because the BIP39 phrase is shown once, never stored and
    /// cannot be reissued — a server-side create hands it to nobody and the organisation becomes
    /// unrecoverable. A platform organisation has no administrator of its own, so the platform
    /// creates it on behalf of the system administrator who is present, and gives them the phrase.
    /// </para>
    /// <para>
    /// <b>The caller MUST surface the returned phrase to that administrator.</b> Dropping it
    /// reintroduces exactly the defect this exists to avoid, silently.
    /// </para>
    /// </remarks>
    Task<PlatformOrgWalletResult?> CreatePlatformOrgWalletAsync(
        Guid organizationId,
        string? name = null,
        string? algorithm = null,
        CancellationToken cancellationToken = default);

    // =========================================================================
    // Org Key Management Operations
    // =========================================================================

    /// <summary>
    /// Provisions a new HD master key for an organisation
    /// </summary>
    /// <param name="orgId">Organisation identifier</param>
    /// <param name="algorithm">Cryptographic algorithm (default ED25519)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Provisioning result with master public key and mnemonic</returns>
    Task<OrgMasterKeyProvisionResponse?> ProvisionOrgMasterKeyAsync(
        string orgId,
        string algorithm = "ED25519",
        CancellationToken ct = default);

    /// <summary>
    /// Derives a user key from an organisation's master key hierarchy
    /// </summary>
    /// <param name="orgId">Organisation identifier</param>
    /// <param name="userId">User subject identifier</param>
    /// <param name="departmentId">Department index in derivation hierarchy</param>
    /// <param name="keyUsage">Intended key usage (Identity, VCIssuance, Governance, Communications, ServiceAuth)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Derived key details</returns>
    Task<DerivedKeyResponse?> DeriveOrgKeyAsync(
        string orgId,
        string userId,
        uint departmentId,
        string keyUsage,
        CancellationToken ct = default);

    /// <summary>
    /// Rotates a derived organisation key by deriving a new key at the next index
    /// </summary>
    /// <param name="orgId">Organisation identifier</param>
    /// <param name="derivedKeyId">ID of the key to rotate</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>New derived key details</returns>
    Task<DerivedKeyResponse?> RotateOrgKeyAsync(
        string orgId,
        Guid derivedKeyId,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a derived organisation key and locks the associated wallet
    /// </summary>
    /// <param name="orgId">Organisation identifier</param>
    /// <param name="derivedKeyId">ID of the key to revoke</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Revocation result</returns>
    Task<RevokeKeyResponse?> RevokeOrgKeyAsync(
        string orgId,
        Guid derivedKeyId,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a wallet signing operation
/// </summary>
public record WalletSignResult
{
    /// <summary>
    /// Raw signature bytes
    /// </summary>
    public required byte[] Signature { get; init; }

    /// <summary>
    /// Derived public key bytes
    /// </summary>
    public required byte[] PublicKey { get; init; }

    /// <summary>
    /// Wallet address that performed the signing
    /// </summary>
    public required string SignedBy { get; init; }

    /// <summary>
    /// Cryptographic algorithm used (ED25519, NISTP256, RSA4096)
    /// </summary>
    public required string Algorithm { get; init; }
}

/// <summary>
/// Feature 181 US4 — result of resolving an org's P-256 certificate-issuing key (public material only).
/// </summary>
/// <param name="Eligible">True when a P-256 key was resolved.</param>
/// <param name="Reason">Machine-readable reason when not eligible (e.g. <c>CERT_KEY_NOT_ELIGIBLE</c>).</param>
/// <param name="BoundKeySource"><c>Primary</c> or <c>HaipCoKey</c> when eligible; null otherwise.</param>
/// <param name="PublicKeySpki">DER SubjectPublicKeyInfo of the P-256 key when eligible; null otherwise.</param>
public sealed record OrgIssuerCertKeyResolution(
    bool Eligible,
    string? Reason,
    string? BoundKeySource,
    byte[]? PublicKeySpki);

/// <summary>
/// Result of a credential issuance operation
/// </summary>
public class CredentialIssuanceResult
{
    /// <summary>
    /// Unique credential identifier (DID URI)
    /// </summary>
    public required string CredentialId { get; init; }

    /// <summary>
    /// Credential type (e.g., "LicenseCredential")
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// DID URI or wallet address of the issuer
    /// </summary>
    public required string IssuerDid { get; init; }

    /// <summary>
    /// DID URI or wallet address of the recipient
    /// </summary>
    public required string SubjectDid { get; init; }

    /// <summary>
    /// All credential claims
    /// </summary>
    public required Dictionary<string, object> Claims { get; init; }

    /// <summary>
    /// When the credential was issued
    /// </summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// When the credential expires (null for no expiry)
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// The complete SD-JWT VC token
    /// </summary>
    public required string RawToken { get; init; }

    /// <summary>
    /// Current credential status (Active, Suspended, Revoked, Expired, Consumed).
    /// </summary>
    public string Status { get; init; } = "Active";

    /// <summary>
    /// URL of the Bitstring Status List containing this credential's revocation/suspension status.
    /// </summary>
    public string? StatusListUrl { get; init; }

    /// <summary>
    /// Index position in the Bitstring Status List.
    /// </summary>
    public int? StatusListIndex { get; init; }

    /// <summary>
    /// Register the credential was issued against. Needed by Blueprint Service
    /// credential-lifecycle endpoints (revoke / suspend / reinstate) to submit
    /// the corresponding <c>CredentialStatusChange</c> register transaction
    /// (multi-node audit CRITICAL #2).
    /// </summary>
    public string? RegisterId { get; init; }

    /// <summary>
    /// Serialised issuer-defined display config (<c>CredentialDisplayConfig</c> JSON) carrying the
    /// authored <c>credentialName</c>. Threaded from the issuance endpoint so the SorchaLocalWallet
    /// register envelope can carry it to the citizen entity. Null when no display name was supplied.
    /// </summary>
    public string? DisplayConfigJson { get; init; }
}

/// <summary>
/// Wallet information returned by the Wallet Service
/// </summary>
/// <summary>
/// A platform organisation's newly-created wallet and its recovery phrase (#1530).
/// </summary>
/// <remarks>
/// <see cref="MnemonicWords"/> is returned ONCE and stored nowhere. It must reach the administrator
/// who triggered the creation, or the organisation cannot ever be recovered.
/// </remarks>
public sealed record PlatformOrgWalletResult(
    Guid OrganizationId,
    string Address,
    string PublicKey,
    string Algorithm,
    string[] MnemonicWords);

public class WalletInfo
{
    /// <summary>
    /// Unique wallet address (public identifier)
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// Human-readable wallet name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Public key (base64-encoded)
    /// </summary>
    public required string PublicKey { get; init; }

    /// <summary>
    /// Cryptographic algorithm (ED25519, NIST-P256, RSA-4096)
    /// </summary>
    public required string Algorithm { get; init; }

    /// <summary>
    /// Wallet status (Active, Revoked, etc.)
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Owner principal
    /// </summary>
    public required string Owner { get; init; }

    /// <summary>
    /// Tenant ID
    /// </summary>
    public required string Tenant { get; init; }

    /// <summary>
    /// When wallet was created
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// When wallet was last updated
    /// </summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// Extensible metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
/// Response from provisioning an organisation master key
/// </summary>
/// <param name="OrganizationId">Organisation that was provisioned</param>
/// <param name="MasterPublicKey">Public key of the master HD key</param>
/// <param name="Mnemonic">BIP39 mnemonic (returned once, must be backed up)</param>
/// <param name="Algorithm">Cryptographic algorithm used</param>
public record OrgMasterKeyProvisionResponse(
    string OrganizationId,
    string MasterPublicKey,
    string Mnemonic,
    string Algorithm);

/// <summary>
/// Response from deriving or rotating an organisation key
/// </summary>
/// <param name="DerivedKeyId">Unique identifier for the derived key record</param>
/// <param name="WalletAddress">Wallet address of the derived key</param>
/// <param name="DerivationPath">BIP32 derivation path used</param>
/// <param name="KeyUsage">Intended key usage purpose</param>
/// <param name="KeyIndex">Index in the derivation hierarchy</param>
/// <param name="Status">Current key status (Active, Rotated, Revoked)</param>
/// <param name="CustodyMode">Key custody mode (ServiceManaged or UserManaged)</param>
/// <param name="CreatedAt">When the key was created</param>
public record DerivedKeyResponse(
    Guid DerivedKeyId,
    string WalletAddress,
    string DerivationPath,
    string KeyUsage,
    uint KeyIndex,
    string Status,
    string CustodyMode,
    DateTime CreatedAt);

/// <summary>
/// Response from revoking a derived organisation key
/// </summary>
/// <param name="DerivedKeyId">ID of the revoked key</param>
/// <param name="Status">Final status (Revoked)</param>
/// <param name="RevokedAt">When the key was revoked</param>
/// <param name="WalletLocked">Whether the associated wallet was locked</param>
/// <param name="DidRevocationPublished">Whether a DID revocation event was published</param>
public record RevokeKeyResponse(
    Guid DerivedKeyId,
    string Status,
    DateTime RevokedAt,
    bool WalletLocked,
    bool DidRevocationPublished);

// =========================================================================
// File Download (Feature 085 — Stored Data Transactions)
// =========================================================================

/// <summary>
/// Result of a file download request containing the file stream and metadata
/// </summary>
/// <param name="FileName">Original filename</param>
/// <param name="ContentType">MIME type of the file</param>
/// <param name="ContentStream">Stream containing the decrypted file content</param>
public record FileDownloadStreamResult(string FileName, string ContentType, Stream ContentStream) : IDisposable
{
    /// <inheritdoc/>
    public void Dispose() => ContentStream.Dispose();
}
