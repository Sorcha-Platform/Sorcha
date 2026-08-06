// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Contracts.Constants;

/// <summary>
/// Predefined Sorcha system derivation paths for specific operations
/// </summary>
/// <remarks>
/// <para>
/// These constants define standard BIP44 paths for common Sorcha operations.
/// Using predefined paths ensures consistency across the system and allows
/// for controlled key derivation for specific purposes.
/// </para>
/// <para>
/// Path format: m/44'/0'/0'/0/{index}
/// - 44' = BIP44 purpose (hardened)
/// - 0' = Coin type (0 for Bitcoin/generic, hardened)
/// - 0' = Account 0 (hardened)
/// - 0 = External chain (receive addresses)
/// - {index} = Address index for specific Sorcha operations
/// </para>
/// <para>
/// <b>This is the single canonical home for every Sorcha derivation context string.</b>
/// It lives in <c>Sorcha.Wallet.Contracts</c> — a zero-dependency leaf assembly — precisely so
/// that every consumer can reference it: the services, the CLI, the Blazor UI, and the WASM
/// wallet PWA alike. It deliberately does <i>not</i> live in <c>Sorcha.Wallet.Portable</c>, whose
/// <c>Sorcha.Cryptography</c> dependency P/Invokes libsodium and cannot load under browser-wasm.
/// </para>
/// <para>
/// <b>Never hard-code a <c>"sorcha:*"</c> context literal at a call site.</b> A typo does not throw —
/// it derives a <i>different but perfectly valid</i> key, so the failure surfaces far away and
/// silently: a mistyped <see cref="DocketSigning"/> yields a validator whose signing key no longer
/// matches its own roster entry, and dockets simply stop sealing. The
/// <c>scripts/check-derivation-contexts.ps1</c> CI gate enforces this.
/// </para>
/// </remarks>
public static class SorchaDerivationPaths
{
    /// <summary>
    /// System path prefix for Sorcha-defined paths
    /// </summary>
    public const string SystemPrefix = "sorcha:";

    /// <summary>
    /// Derivation path for register attestation signing — an organisation's <b>governance key</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used when owners/admins sign attestations to approve register creation, and thereafter
    /// whenever that organisation signs a <b>governance control transaction</b> on that register
    /// (crypto-policy updates, roster proposals, approvals). Maps to: m/44'/0'/0'/0/100
    /// </para>
    /// <para>
    /// <b>The roster records the key derived here, so governance MUST sign with it.</b> A register's
    /// governance roster is the set of attestation public keys captured in its genesis control
    /// record; the Validator's <c>RightsEnforcementService</c> authorises a control transaction by
    /// matching the transaction's signing key against that set. Sign with anything else — the
    /// wallet's primary key, or the node's system wallet at <see cref="RegisterControl"/>
    /// (slot 101) — and the transaction is rejected "submitter not found in roster".
    /// </para>
    /// <para>
    /// <b>History (Feature 189, clean break).</b> Only ONE of the four register-creation paths used
    /// this constant — the admin UI's <c>CreateRegisterWizard</c>. The CLI, the F142 sandbox
    /// provider and the walkthrough module all passed <b>no derivation path at all</b>, so their
    /// attestations were signed with the wallet's <i>primary</i> key. A register was therefore
    /// governable or not depending on which tool created it, and nothing detected the difference:
    /// both produce a valid signature and a well-formed roster, and the divergence only surfaces
    /// later as "submitter not found in roster" on the first governance operation. All four paths
    /// now sign here.
    /// </para>
    /// <para>
    /// Separating the governance key from the wallet's general-purpose key lets an organisation
    /// rotate or delegate governance authority without disturbing its identity key. Because the
    /// roster is baked into the immutable genesis, the correction applies only to registers created
    /// after it — registers created by the CLI, sandbox or walkthrough before it carry primary-key
    /// rosters and must be recreated (and the network re-genesised for the system register) to be
    /// governable.
    /// </para>
    /// </remarks>
    public const string RegisterAttestation = "sorcha:register-attestation";

    /// <summary>
    /// BIP44 path for register attestation signing
    /// </summary>
    public const string RegisterAttestationPath = "m/44'/0'/0'/0/100";

    /// <summary>
    /// Derivation path for control record signing (system wallet only)
    /// </summary>
    /// <remarks>
    /// Used by the Validator service system wallet to sign complete control records
    /// after all attestations are collected.
    /// Maps to: m/44'/0'/0'/0/101
    /// </remarks>
    public const string RegisterControl = "sorcha:register-control";

    /// <summary>
    /// BIP44 path for control record signing
    /// </summary>
    public const string RegisterControlPath = "m/44'/0'/0'/0/101";

    /// <summary>
    /// Derivation path for docket signing (system wallet only)
    /// </summary>
    /// <remarks>
    /// Used by the Validator service to sign dockets after transaction validation.
    /// Maps to: m/44'/0'/0'/0/102
    /// </remarks>
    public const string DocketSigning = "sorcha:docket-signing";

    /// <summary>
    /// BIP44 path for docket signing
    /// </summary>
    public const string DocketSigningPath = "m/44'/0'/0'/0/102";

    /// <summary>
    /// Derivation path for blueprint publishing to the system register
    /// </summary>
    /// <remarks>
    /// Used by the Register Service system wallet to sign blueprint transactions
    /// published to the system register.
    /// Maps to: m/44'/0'/0'/0/103
    /// </remarks>
    public const string BlueprintPublish = "sorcha:blueprint-publish";

    /// <summary>
    /// BIP44 path for blueprint publishing
    /// </summary>
    public const string BlueprintPublishPath = "m/44'/0'/0'/0/103";

    /// <summary>
    /// Derivation path for per-user persona vault encryption
    /// </summary>
    /// <remarks>
    /// Used by the Wallet Service to derive a symmetric key that encrypts a
    /// PlatformUser's self-asserted identity attributes ("persona") at rest.
    /// The ciphertext lives in the Tenant Service; the key material is derived
    /// on demand under this purpose and never stored alongside the ciphertext.
    /// Maps to: m/44'/0'/0'/0/104
    /// </remarks>
    public const string PersonaVault = "sorcha:persona-vault";

    /// <summary>
    /// BIP44 path reserved for persona vault encryption. <b>Not exercised
    /// in v1.</b> The current <c>PersonaCryptoService</c> uses HKDF-SHA256
    /// with <see cref="PersonaVault"/> as the <c>info</c> parameter rather
    /// than hierarchical BIP44 derivation; this constant is registered in
    /// <see cref="ResolvePath"/> so a future HD-derivation refactor can
    /// switch over without having to allocate a new reserved index.
    /// </summary>
    public const string PersonaVaultPath = "m/44'/0'/0'/0/104";

    /// <summary>
    /// Derivation path for credential holder binding key (KB-JWT signing)
    /// </summary>
    /// <remarks>
    /// Used by the Wallet Service to derive a per-wallet key that proves
    /// holder possession of a credential via a Key Binding JWT (KB-JWT).
    /// One key per wallet, not per credential. The public half is embedded
    /// in the credential's <c>cnf</c> claim at issuance; the private half
    /// signs KB-JWTs at presentation time.
    /// Maps to: m/44'/0'/0'/0/105
    /// </remarks>
    public const string CredentialHolderBinding = "sorcha:credential-holder-binding";

    /// <summary>
    /// BIP44 path for credential holder binding key
    /// </summary>
    public const string CredentialHolderBindingPath = "m/44'/0'/0'/0/105";

    /// <summary>
    /// Derivation path for HAIP-facing classical issuer co-key
    /// </summary>
    /// <remarks>
    /// Used by wallets whose primary algorithm is PQC (ML-DSA, SLH-DSA) to
    /// derive a classical signing key (ES256 by default) for signing
    /// HAIP-conformant SD-JWT VCs. External HAIP wallets require classical
    /// signatures; Sorcha-internal transactions continue using the primary
    /// PQC key. Wallets whose primary key is already classical do not derive
    /// a co-key under this purpose.
    /// Maps to: m/44'/0'/0'/0/106
    /// </remarks>
    public const string HaipIssuerSigning = "sorcha:haip-issuer-signing";

    /// <summary>
    /// BIP44 path for HAIP issuer classical co-key
    /// </summary>
    public const string HaipIssuerSigningPath = "m/44'/0'/0'/0/106";

    /// <summary>
    /// Derivation path for tenant-level CA signing key
    /// </summary>
    /// <remarks>
    /// Used by the Tenant Service to derive a key for signing the tenant's
    /// self-signed root CA certificate and organisation certificates.
    /// Maps to: m/44'/0'/0'/0/107
    /// </remarks>
    public const string TenantCaSigning = "sorcha:tenant-ca-signing";

    /// <summary>
    /// BIP44 path for tenant CA signing key
    /// </summary>
    public const string TenantCaSigningPath = "m/44'/0'/0'/0/107";

    /// <summary>
    /// Derivation path for the citizen wallet holder identity (Feature 114)
    /// </summary>
    /// <remarks>
    /// Used by the Wallet Service to derive a per-PlatformUser holder key under which
    /// citizen-wallet credentials are bound (via the credential's <c>cnf</c> claim) and
    /// which signs device-delegation credentials issued to enrolled wallet devices.
    /// Distinct from <see cref="CredentialHolderBinding"/> (slot 105): that is per-wallet
    /// for KB-JWT signing on the existing online HAIP path. Slot 108 is the citizen's
    /// stable cross-device identity for the offline-first wallet model.
    /// Maps to: m/44'/0'/0'/0/108
    /// </remarks>
    public const string CitizenHolder = "sorcha:citizen-holder";

    /// <summary>
    /// BIP44 path for the citizen wallet holder identity
    /// </summary>
    public const string CitizenHolderPath = "m/44'/0'/0'/0/108";

    /// <summary>
    /// Derivation path for the per-org citizen device status-list signing key (Feature 114)
    /// </summary>
    /// <remarks>
    /// Used by the Wallet Service to sign Token Status List 2024 JWTs that publish the
    /// revocation status of citizen wallet device delegations. One derived key per tenant
    /// org, signing all citizen-device status lists for that org. Separated from the
    /// org root wallet to apply least-privilege to a frequently-used signing operation.
    /// Maps to: m/44'/0'/0'/0/109
    /// </remarks>
    public const string CitizenStatusSigning = "sorcha:citizen-status-signing";

    /// <summary>
    /// BIP44 path for the citizen device status-list signing key
    /// </summary>
    public const string CitizenStatusSigningPath = "m/44'/0'/0'/0/109";

    /// <summary>
    /// Resolves a Sorcha system path to its corresponding BIP44 path
    /// </summary>
    /// <param name="systemPath">Sorcha system path (e.g., "sorcha:register-attestation")</param>
    /// <returns>BIP44 derivation path string</returns>
    /// <exception cref="ArgumentException">Thrown when system path is not recognized</exception>
    public static string ResolvePath(string systemPath)
    {
        if (string.IsNullOrWhiteSpace(systemPath))
            throw new ArgumentException("System path cannot be empty", nameof(systemPath));

        // If it's already a BIP44 path, return as-is
        if (systemPath.StartsWith("m/", StringComparison.OrdinalIgnoreCase))
            return systemPath;

        // Resolve Sorcha system paths
        return systemPath.ToLowerInvariant() switch
        {
            RegisterAttestation => RegisterAttestationPath,
            RegisterControl => RegisterControlPath,
            DocketSigning => DocketSigningPath,
            BlueprintPublish => BlueprintPublishPath,
            PersonaVault => PersonaVaultPath,
            CredentialHolderBinding => CredentialHolderBindingPath,
            HaipIssuerSigning => HaipIssuerSigningPath,
            TenantCaSigning => TenantCaSigningPath,
            CitizenHolder => CitizenHolderPath,
            CitizenStatusSigning => CitizenStatusSigningPath,
            _ => throw new ArgumentException($"Unknown Sorcha system path: {systemPath}", nameof(systemPath))
        };
    }

    /// <summary>
    /// Checks if a path string is a Sorcha system path
    /// </summary>
    /// <param name="path">Path to check</param>
    /// <returns>True if the path is a Sorcha system path</returns>
    public static bool IsSystemPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               path.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
