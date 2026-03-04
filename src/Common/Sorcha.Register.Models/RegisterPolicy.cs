// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// Per-register operational policy governing governance, validators, consensus,
/// and leader election. Embedded in control transaction payloads and upgradeable
/// via governance proposals.
/// </summary>
public class RegisterPolicy
{
    /// <summary>
    /// Policy version (monotonically increasing). Must be >= 1 and > previous version.
    /// </summary>
    [JsonPropertyName("version")]
    public uint Version { get; set; }

    /// <summary>
    /// Governance configuration controlling quorum rules and proposal lifecycle.
    /// </summary>
    [JsonPropertyName("governance")]
    public PolicyGovernanceConfig Governance { get; set; } = new();

    /// <summary>
    /// Validator configuration controlling registration, approval, and operational parameters.
    /// </summary>
    [JsonPropertyName("validators")]
    public PolicyValidatorConfig Validators { get; set; } = new();

    /// <summary>
    /// Consensus configuration controlling signature thresholds and docket building.
    /// </summary>
    [JsonPropertyName("consensus")]
    public PolicyConsensusConfig Consensus { get; set; } = new();

    /// <summary>
    /// Leader election configuration controlling mechanism, heartbeat, and term duration.
    /// </summary>
    [JsonPropertyName("leaderElection")]
    public PolicyLeaderElectionConfig LeaderElection { get; set; } = new();

    /// <summary>
    /// UTC timestamp when this policy version was last updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// DID of the participant who last updated this policy. Null for genesis policy.
    /// </summary>
    [JsonPropertyName("updatedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Creates the default register policy for new registers.
    /// Returns a fully populated policy with all default configuration values.
    /// </summary>
    public static RegisterPolicy CreateDefault()
    {
        var now = DateTimeOffset.UtcNow;

        return new RegisterPolicy
        {
            Version = 1,
            Governance = new PolicyGovernanceConfig
            {
                QuorumFormula = QuorumFormula.StrictMajority,
                ProposalTtlDays = 7,
                OwnerCanBypassQuorum = true,
                BlueprintVersion = "register-governance-v1"
            },
            Validators = new PolicyValidatorConfig
            {
                RegistrationMode = RegistrationMode.Public,
                ApprovedValidators = [],
                MinValidators = 1,
                MaxValidators = 100,
                RequireStake = false,
                StakeAmount = null,
                OperationalTtlSeconds = 60
            },
            Consensus = new PolicyConsensusConfig
            {
                SignatureThresholdMin = 2,
                SignatureThresholdMax = 10,
                MaxTransactionsPerDocket = 1000,
                DocketBuildIntervalMs = 100,
                DocketTimeoutSeconds = 30
            },
            LeaderElection = new PolicyLeaderElectionConfig
            {
                Mechanism = ElectionMechanism.Rotating,
                HeartbeatIntervalMs = 1000,
                LeaderTimeoutMs = 5000,
                TermDurationSeconds = 60
            },
            UpdatedAt = now,
            UpdatedBy = null
        };
    }
}

/// <summary>
/// Governance configuration controlling quorum rules, proposal lifecycle,
/// and owner override capabilities.
/// </summary>
public class PolicyGovernanceConfig
{
    /// <summary>
    /// Formula used to calculate quorum requirements for governance proposals.
    /// </summary>
    [JsonPropertyName("quorumFormula")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public QuorumFormula QuorumFormula { get; set; } = QuorumFormula.StrictMajority;

    /// <summary>
    /// Number of days a governance proposal remains active before expiring.
    /// Must be >= 1 and &lt;= 90.
    /// </summary>
    [JsonPropertyName("proposalTtlDays")]
    public int ProposalTtlDays { get; set; } = 7;

    /// <summary>
    /// Whether the register owner can bypass quorum requirements.
    /// </summary>
    [JsonPropertyName("ownerCanBypassQuorum")]
    public bool OwnerCanBypassQuorum { get; set; } = true;

    /// <summary>
    /// Version identifier for the governance blueprint schema.
    /// Must be non-empty and at most 100 characters.
    /// </summary>
    [JsonPropertyName("blueprintVersion")]
    public string BlueprintVersion { get; set; } = "register-governance-v1";
}

/// <summary>
/// Validator configuration controlling registration mode, approval lists,
/// capacity limits, staking, and operational parameters.
/// </summary>
public class PolicyValidatorConfig
{
    /// <summary>
    /// How validators are registered: Public (open) or Approved (permissioned).
    /// </summary>
    [JsonPropertyName("registrationMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegistrationMode RegistrationMode { get; set; } = RegistrationMode.Public;

    /// <summary>
    /// List of pre-approved validators when <see cref="RegistrationMode"/> is Approved.
    /// Maximum 100 entries.
    /// </summary>
    [JsonPropertyName("approvedValidators")]
    public List<ApprovedValidator> ApprovedValidators { get; set; } = [];

    /// <summary>
    /// Minimum number of validators required for the register to operate.
    /// Must be >= 1.
    /// </summary>
    [JsonPropertyName("minValidators")]
    public int MinValidators { get; set; } = 1;

    /// <summary>
    /// Maximum number of validators allowed on the register.
    /// Must be >= <see cref="MinValidators"/> and &lt;= 100.
    /// </summary>
    [JsonPropertyName("maxValidators")]
    public int MaxValidators { get; set; } = 100;

    /// <summary>
    /// Whether validators must stake tokens to participate.
    /// </summary>
    [JsonPropertyName("requireStake")]
    public bool RequireStake { get; set; }

    /// <summary>
    /// Amount of stake required when <see cref="RequireStake"/> is true.
    /// Must be > 0 when staking is required.
    /// </summary>
    [JsonPropertyName("stakeAmount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? StakeAmount { get; set; }

    /// <summary>
    /// Time-to-live in seconds for a validator's operational heartbeat.
    /// Must be >= 10 and &lt;= 600.
    /// </summary>
    [JsonPropertyName("operationalTtlSeconds")]
    public int OperationalTtlSeconds { get; set; } = 60;
}

/// <summary>
/// An approved validator entry containing identity and approval metadata.
/// </summary>
public class ApprovedValidator
{
    /// <summary>
    /// Decentralized identifier (DID) of the approved validator.
    /// Must be in DID format and at most 255 characters.
    /// </summary>
    [JsonPropertyName("did")]
    public string Did { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded public key of the approved validator.
    /// </summary>
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when this validator was approved.
    /// </summary>
    [JsonPropertyName("approvedAt")]
    public DateTimeOffset ApprovedAt { get; set; }

    /// <summary>
    /// DID of the participant who approved this validator. Null for genesis entries.
    /// </summary>
    [JsonPropertyName("approvedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApprovedBy { get; set; }
}

/// <summary>
/// Consensus configuration controlling signature thresholds, docket capacity,
/// and timing parameters.
/// </summary>
public class PolicyConsensusConfig
{
    /// <summary>
    /// Minimum number of validator signatures required per docket.
    /// Must be >= 1.
    /// </summary>
    [JsonPropertyName("signatureThresholdMin")]
    public int SignatureThresholdMin { get; set; } = 2;

    /// <summary>
    /// Maximum number of validator signatures accepted per docket.
    /// Must be >= <see cref="SignatureThresholdMin"/>.
    /// </summary>
    [JsonPropertyName("signatureThresholdMax")]
    public int SignatureThresholdMax { get; set; } = 10;

    /// <summary>
    /// Maximum number of transactions that can be batched into a single docket.
    /// Must be >= 1 and &lt;= 10000.
    /// </summary>
    [JsonPropertyName("maxTransactionsPerDocket")]
    public int MaxTransactionsPerDocket { get; set; } = 1000;

    /// <summary>
    /// Interval in milliseconds between docket build cycles.
    /// Must be >= 10 and &lt;= 60000.
    /// </summary>
    [JsonPropertyName("docketBuildIntervalMs")]
    public int DocketBuildIntervalMs { get; set; } = 100;

    /// <summary>
    /// Timeout in seconds for docket completion before retry.
    /// Must be >= 5 and &lt;= 300.
    /// </summary>
    [JsonPropertyName("docketTimeoutSeconds")]
    public int DocketTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Leader election configuration controlling the mechanism, heartbeat intervals,
/// timeout thresholds, and term duration.
/// </summary>
public class PolicyLeaderElectionConfig
{
    /// <summary>
    /// Mechanism used for leader election among validators.
    /// </summary>
    [JsonPropertyName("mechanism")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ElectionMechanism Mechanism { get; set; } = ElectionMechanism.Rotating;

    /// <summary>
    /// Interval in milliseconds between leader heartbeat messages.
    /// Must be >= 100 and &lt;= 30000.
    /// </summary>
    [JsonPropertyName("heartbeatIntervalMs")]
    public int HeartbeatIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Timeout in milliseconds before a leader is considered unresponsive.
    /// Must be > <see cref="HeartbeatIntervalMs"/>.
    /// </summary>
    [JsonPropertyName("leaderTimeoutMs")]
    public int LeaderTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Duration in seconds for a leader's term. Null for indefinite terms.
    /// Must be >= 10 when set.
    /// </summary>
    [JsonPropertyName("termDurationSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TermDurationSeconds { get; set; } = 60;
}
