// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// Represents a register control record with cryptographic attestations
/// establishing administrative control and ownership of a register.
/// </summary>
/// <remarks>
/// The control record is stored in the genesis transaction and provides
/// an immutable audit trail of who created and controls the register.
/// All attestations must be cryptographically signed by the subject's wallet.
/// </remarks>
public class RegisterControlRecord
{
    /// <summary>
    /// Unique register identifier (GUID without hyphens)
    /// </summary>
    [Required]
    [StringLength(32, MinimumLength = 32)]
    [RegularExpression("^[a-f0-9]{32}$", ErrorMessage = "Register ID must be a 32-character hex string")]
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable register name
    /// </summary>
    [Required]
    [StringLength(38, MinimumLength = 1)]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Purpose and scope of the register
    /// </summary>
    [StringLength(500)]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Owning tenant/organization identifier
    /// </summary>
    [Required]
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// ISO 8601 creation timestamp (UTC)
    /// </summary>
    [Required]
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Cryptographic attestations of administrative roles
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one attestation (owner) is required")]
    [MaxLength(25, ErrorMessage = "Maximum 25 attestations allowed")]
    [JsonPropertyName("attestations")]
    public List<RegisterAttestation> Attestations { get; set; } = new();

    /// <summary>
    /// Cryptographic policy governing which algorithms are accepted on this register.
    /// Null for backward compatibility with pre-PQC registers (defaults apply).
    /// </summary>
    [JsonPropertyName("cryptoPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CryptoPolicy? CryptoPolicy { get; set; }

    /// <summary>
    /// Operational policy governing validators, consensus, governance, and leader election.
    /// Null for backward compatibility with pre-feature registers (defaults apply at read time).
    /// </summary>
    [JsonPropertyName("registerPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RegisterPolicy? RegisterPolicy { get; set; }

    /// <summary>
    /// Additional register metadata (tags, category, etc.)
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Validates that at least one owner attestation exists
    /// </summary>
    public bool HasOwnerAttestation()
    {
        return Attestations.Any(a => a.Role == RegisterRole.Owner);
    }

    /// <summary>
    /// Gets all subjects with a specific role
    /// </summary>
    public IEnumerable<string> GetSubjectsWithRole(RegisterRole role)
    {
        return Attestations
            .Where(a => a.Role == role)
            .Select(a => a.Subject);
    }

    /// <summary>
    /// Gets all voting members (Owner + Admin roles only)
    /// </summary>
    public IEnumerable<RegisterAttestation> GetVotingMembers()
    {
        return Attestations.Where(a => a.Role is RegisterRole.Owner or RegisterRole.Admin);
    }

    /// <summary>
    /// Calculates the quorum threshold for the voting pool using the specified formula.
    /// </summary>
    /// <param name="excludeDid">Optional DID to exclude from the voting pool (e.g., removal target)</param>
    /// <param name="formula">Quorum formula to apply (defaults to StrictMajority for backward compatibility)</param>
    /// <returns>The number of approvals required for quorum</returns>
    public int GetQuorumThreshold(string? excludeDid = null, QuorumFormula formula = QuorumFormula.StrictMajority)
    {
        var votingMembers = GetVotingMembers();
        if (excludeDid is not null)
        {
            votingMembers = votingMembers.Where(a => a.Subject != excludeDid);
        }

        var m = votingMembers.Count();
        return CalculateQuorum(m, formula);
    }

    /// <summary>
    /// Calculates the quorum threshold for a given voting pool size and formula.
    /// </summary>
    /// <param name="votingPoolSize">Number of eligible voting members</param>
    /// <param name="formula">Quorum formula to apply</param>
    /// <returns>The number of approvals required for quorum (minimum 1)</returns>
    public static int CalculateQuorum(int votingPoolSize, QuorumFormula formula)
    {
        if (votingPoolSize <= 0) return 1;

        return formula switch
        {
            QuorumFormula.StrictMajority => (votingPoolSize / 2) + 1,
            QuorumFormula.Supermajority => ((2 * votingPoolSize) / 3) + 1,
            QuorumFormula.Unanimous => votingPoolSize,
            _ => (votingPoolSize / 2) + 1
        };
    }
}

/// <summary>
/// Cryptographic attestation of an administrative role in a register
/// </summary>
public class RegisterAttestation
{
    /// <summary>
    /// Administrative role being attested
    /// </summary>
    [Required]
    [JsonPropertyName("role")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegisterRole Role { get; set; }

    /// <summary>
    /// DID or user identifier (e.g., did:sorcha:user-123)
    /// </summary>
    [Required]
    [StringLength(255)]
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded public key for verification
    /// </summary>
    [Required]
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Cryptographic signature of the control record hash
    /// </summary>
    [Required]
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// Signature algorithm used
    /// </summary>
    [Required]
    [JsonPropertyName("algorithm")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SignatureAlgorithm Algorithm { get; set; }

    /// <summary>
    /// When this role was granted (UTC)
    /// </summary>
    [Required]
    [JsonPropertyName("grantedAt")]
    public DateTimeOffset GrantedAt { get; set; }
}

/// <summary>
/// Administrative roles within a register
/// </summary>
public enum RegisterRole
{
    /// <summary>
    /// Full ownership and control (can add/remove admins)
    /// </summary>
    Owner,

    /// <summary>
    /// Administrative access (can manage register settings)
    /// </summary>
    Admin,

    /// <summary>
    /// Audit access (read-only, full history access)
    /// </summary>
    Auditor,

    /// <summary>
    /// Blueprint design access (can modify workflows)
    /// </summary>
    Designer
}

/// <summary>
/// Cryptographic signature algorithms supported
/// </summary>
public enum SignatureAlgorithm
{
    /// <summary>
    /// Edwards-curve Digital Signature Algorithm (ED25519)
    /// </summary>
    ED25519,

    /// <summary>
    /// NIST P-256 (secp256r1) elliptic curve
    /// </summary>
    NISTP256,

    /// <summary>
    /// RSA with 4096-bit keys
    /// </summary>
    RSA4096,

    /// <summary>
    /// ML-DSA-65 (FIPS 204) — lattice-based post-quantum signature
    /// </summary>
    ML_DSA_65,

    /// <summary>
    /// SLH-DSA-128s (FIPS 205) — hash-based post-quantum signature
    /// </summary>
    SLH_DSA_128s
}
