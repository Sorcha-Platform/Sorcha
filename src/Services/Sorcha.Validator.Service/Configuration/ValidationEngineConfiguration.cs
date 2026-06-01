// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Configuration;

/// <summary>
/// Configuration for the Validation Engine
/// </summary>
public class ValidationEngineConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings
    /// </summary>
    public const string SectionName = "ValidationEngine";

    /// <summary>
    /// Maximum transactions to validate in a single batch
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Interval between validation batch runs
    /// </summary>
    public TimeSpan ValidationInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Maximum concurrent validations per register
    /// </summary>
    public int MaxConcurrentValidationsPerRegister { get; set; } = 10;

    /// <summary>
    /// Enable parallel validation within a batch
    /// </summary>
    public bool EnableParallelValidation { get; set; } = true;

    /// <summary>
    /// Maximum retries for failed validations (transient errors)
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Timeout for individual transaction validation
    /// </summary>
    public TimeSpan ValidationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Enable schema validation against blueprint
    /// </summary>
    public bool EnableSchemaValidation { get; set; } = true;

    /// <summary>
    /// Enable cryptographic signature verification
    /// </summary>
    public bool EnableSignatureVerification { get; set; } = true;

    /// <summary>
    /// Enable chain validation (previousId linking)
    /// </summary>
    public bool EnableChainValidation { get; set; } = true;

    /// <summary>
    /// Maximum allowed clock skew for transaction timestamps
    /// </summary>
    public TimeSpan MaxClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether to enable validation metrics collection
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Maximum age of a transaction before rejection
    /// </summary>
    public TimeSpan MaxTransactionAge { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Maximum age of a genesis transaction before rejection (VAL_TIME_002).
    /// </summary>
    /// <remarks>
    /// SECURITY: the genesis trust anchor is pre-signed offline, so a stale-but-still-accepted
    /// genesis is a replay vector — an attacker could push an old (e.g. superseded) genesis to
    /// seed or hijack a freshly-bootstrapping node. Bounding genesis freshness to a short window
    /// forces a regenerated system register to be minted, embedded, deployed, and bootstrapped
    /// within that window. This applies to a node that <b>ingests + seals</b> a pre-signed
    /// genesis (Auto bootstrap); a node that <b>pulls an already-sealed genesis docket</b> from a
    /// peer verifies the sealed docket's validator signature + chain, not the genesis
    /// transaction's age, so late-joining replicas are unaffected. Default 1 hour.
    /// </remarks>
    public TimeSpan GenesisMaxAge { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Enable governance rights enforcement for Control transactions.
    /// When enabled, Control transactions are validated against the register's
    /// admin roster to ensure the submitter has the required role.
    /// </summary>
    public bool EnableGovernanceValidation { get; set; } = true;

    /// <summary>
    /// Enable blueprint conformance validation (sender authorization,
    /// starting action validation, action sequencing via routes).
    /// </summary>
    public bool EnableBlueprintConformance { get; set; } = true;

    /// <summary>
    /// Enable crypto policy validation against the register's active CryptoPolicy.
    /// When enabled, transaction signature algorithms are checked against accepted
    /// and required algorithms defined in the register's crypto policy.
    /// </summary>
    public bool EnableCryptoPolicyValidation { get; set; } = true;

    /// <summary>
    /// Enable structural file reference validation for transactions whose payload
    /// contains <c>file-reference</c> fields.
    /// </summary>
    /// <remarks>
    /// At transaction-submission time this enforces structural constraints only
    /// (required fields, hash format, chunk count limits, size caps, content-type
    /// allowlist).  Full per-chunk integrity verification (<c>FileChunkValidationRule</c>)
    /// is deferred to docket-sealing time where all referenced chunk transactions are
    /// locally available without additional network round-trips.
    /// </remarks>
    public bool EnableFileReferenceValidation { get; set; } = true;

    /// <summary>
    /// Enable routing-decision validation (Feature 145, <c>VAL_ROUTING_001/002</c>).
    /// When enabled, an action transaction that carries a <c>routingDecision</c> is checked:
    /// every next action must be a structural successor of the completed action in the
    /// published route graph, and the carried attestation must verify and satisfy the
    /// register's <c>routingAttestation</c> governance policy. Transactions that do not
    /// carry a decision are unaffected.
    /// </summary>
    public bool EnableRoutingValidation { get; set; } = true;
}
