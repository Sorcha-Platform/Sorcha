// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// Per-register cryptographic policy governing which algorithms are accepted.
/// Embedded in control transaction payloads and upgradeable via governance.
/// </summary>
public class CryptoPolicy
{
    /// <summary>
    /// Policy version (monotonically increasing). Must be >= 1 and > previous version.
    /// </summary>
    [JsonPropertyName("version")]
    public uint Version { get; set; }

    /// <summary>
    /// Algorithm identifiers accepted for transaction signing.
    /// At least one must be specified.
    /// </summary>
    [JsonPropertyName("acceptedSignatureAlgorithms")]
    public string[] AcceptedSignatureAlgorithms { get; set; } = [];

    /// <summary>
    /// Algorithms that MUST be present on every transaction.
    /// Must be a subset of <see cref="AcceptedSignatureAlgorithms"/>.
    /// </summary>
    [JsonPropertyName("requiredSignatureAlgorithms")]
    public string[] RequiredSignatureAlgorithms { get; set; } = [];

    /// <summary>
    /// Encryption schemes accepted for payload encryption.
    /// At least one must be specified.
    /// </summary>
    [JsonPropertyName("acceptedEncryptionSchemes")]
    public string[] AcceptedEncryptionSchemes { get; set; } = [];

    /// <summary>
    /// Hash functions accepted for TxId computation.
    /// At least one must be specified.
    /// </summary>
    [JsonPropertyName("acceptedHashFunctions")]
    public string[] AcceptedHashFunctions { get; set; } = [];

    /// <summary>
    /// Enforcement mode: Permissive (warn) or Strict (reject).
    /// </summary>
    [JsonPropertyName("enforcementMode")]
    public CryptoPolicyEnforcementMode EnforcementMode { get; set; }

    /// <summary>
    /// UTC timestamp when this policy version takes effect.
    /// </summary>
    [JsonPropertyName("effectiveFrom")]
    public DateTime EffectiveFrom { get; set; }

    /// <summary>
    /// Algorithms being phased out. Transactions using these generate warnings.
    /// </summary>
    [JsonPropertyName("deprecatedAlgorithms")]
    public string[] DeprecatedAlgorithms { get; set; } = [];

    /// <summary>
    /// DevMode: when <c>true</c>, the register skips field-level payload encryption and stores
    /// payloads as plaintext (still disclosure-filtered, still routed through JSON-Logic and
    /// calculations). A development / debugging posture for verifying workflow logic without the
    /// crypto layer; receivers read the plaintext directly without decrypting.
    /// </summary>
    /// <remarks>
    /// This lives in <see cref="CryptoPolicy"/> — an encryption posture sibling of
    /// <see cref="AcceptedEncryptionSchemes"/> — so it is baked into the immutable genesis and
    /// replicates to every node, and so changes ride the existing crypto-policy update control
    /// record (a SyncOnly replica reads it from the synced genesis / later control records and
    /// respects the plaintext posture). The transition is <b>one-way</b>: a register may be
    /// promoted DevMode→Normal (plaintext→encrypted) via a crypto-policy update, but never
    /// Normal→DevMode — validators reject a policy update that re-enables DevMode. Default
    /// <c>false</c> (encrypted / production). NEVER enable in production.
    /// </remarks>
    [JsonPropertyName("devMode")]
    public bool DevMode { get; set; }

    /// <summary>
    /// Creates the default crypto policy for new registers.
    /// Accepts all algorithms, requires none, permissive enforcement.
    /// </summary>
    public static CryptoPolicy CreateDefault()
    {
        return new CryptoPolicy
        {
            Version = 1,
            AcceptedSignatureAlgorithms = ["ED25519", "NISTP256", "RSA4096", "ML-DSA-65", "SLH-DSA-128s"],
            RequiredSignatureAlgorithms = [],
            AcceptedEncryptionSchemes = ["XCHACHA20-POLY1305", "AES-256-GCM", "ML-KEM-768"],
            AcceptedHashFunctions = ["SHA-256", "BLAKE2B-256"],
            EnforcementMode = CryptoPolicyEnforcementMode.Permissive,
            EffectiveFrom = DateTime.UtcNow,
            DeprecatedAlgorithms = []
        };
    }

    /// <summary>
    /// Validates this policy for internal consistency.
    /// </summary>
    public bool IsValid()
    {
        if (Version < 1)
            return false;

        if (AcceptedSignatureAlgorithms.Length == 0)
            return false;

        if (AcceptedEncryptionSchemes.Length == 0)
            return false;

        if (AcceptedHashFunctions.Length == 0)
            return false;

        // RequiredSignatureAlgorithms must be a subset of AcceptedSignatureAlgorithms
        if (RequiredSignatureAlgorithms.Length > 0)
        {
            var accepted = new HashSet<string>(AcceptedSignatureAlgorithms, StringComparer.OrdinalIgnoreCase);
            foreach (var required in RequiredSignatureAlgorithms)
            {
                if (!accepted.Contains(required))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Known algorithm identifiers used across crypto policies.
    /// </summary>
    public static class KnownAlgorithms
    {
        public const string ED25519 = "ED25519";
        public const string NISTP256 = "NISTP256";
        public const string RSA4096 = "RSA4096";
        public const string ML_DSA_65 = "ML-DSA-65";
        public const string SLH_DSA_128s = "SLH-DSA-128s";
        public const string ML_KEM_768 = "ML-KEM-768";
        public const string XCHACHA20_POLY1305 = "XCHACHA20-POLY1305";
        public const string AES_256_GCM = "AES-256-GCM";
        public const string SHA_256 = "SHA-256";
        public const string SHA_512 = "SHA-512";
        public const string BLAKE2B_256 = "BLAKE2B-256";

        /// <summary>
        /// All known algorithm identifiers.
        /// </summary>
        public static readonly string[] All =
        [
            ED25519, NISTP256, RSA4096, ML_DSA_65, SLH_DSA_128s, ML_KEM_768,
            XCHACHA20_POLY1305, AES_256_GCM, SHA_256, SHA_512, BLAKE2B_256
        ];
    }
}

/// <summary>
/// Policy enforcement mode for cryptographic operations.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CryptoPolicyEnforcementMode
{
    /// <summary>
    /// Non-compliant transactions generate warnings but are accepted.
    /// </summary>
    Permissive,

    /// <summary>
    /// Non-compliant transactions are rejected.
    /// </summary>
    Strict
}
