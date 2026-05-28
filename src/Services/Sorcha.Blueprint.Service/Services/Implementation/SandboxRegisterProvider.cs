// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 142 (T026 / D1) — lazily provisions and reuses one devMode sandbox register per
/// organisation. The per-org register id is cached in-memory (transient — Redis is a future swap)
/// and reused across rehearsals. A creation is serialised per organisation so concurrent rehearsal
/// starts share a single sandbox register rather than racing to create duplicates.
/// </summary>
/// <remarks>
/// <para>
/// Register creation is the platform's two-phase owner-attestation ceremony, not a bare POST:
/// <list type="number">
/// <item><c>POST /api/registers/initiate</c> returns one attestation hash per owner.</item>
/// <item>Each hash is signed with the org's sandbox-owner wallet (pre-hashed signing).</item>
/// <item><c>POST /api/registers/finalize</c> submits the signed attestations; the server verifies
/// them, seals the genesis transaction, and creates the register.</item>
/// </list>
/// The signing flow mirrors the CLI's <c>RegisterCreateCommand</c> (the canonical server-side
/// reference): convert the hex <see cref="AttestationToSign.DataToSign"/> hash to bytes, sign
/// pre-hashed, and base64-encode the signature + public key into the
/// <see cref="SignedAttestation"/> (the orchestrator verifies via base64-auto decode).
/// </para>
/// <para>
/// Sandbox registers are created in <b>devMode</b> (plaintext payloads with disclosure filtering,
/// no envelope encryption — fast, throwaway), <b>not advertised</b>, and tagged
/// <c>Metadata["sandbox"] = "true"</c> so the Go-live picker and normal listings exclude them
/// (the computed <c>Register.Sandbox</c> flag from T009). A "reset" of a rehearsal discards the
/// rehearsal instance and the ephemeral identities, never the register — so the same sandbox
/// register backs every rehearsal an org ever runs.
/// </para>
/// <para>
/// Registered as a singleton because the per-org cache is process-wide transient state.
/// </para>
/// </remarks>
public sealed class SandboxRegisterProvider : ISandboxRegisterProvider
{
    // Scoped clients (IRegisterServiceClient / IWalletServiceClient) are resolved per-operation
    // from a fresh scope: this provider is a singleton (its per-org caches are process-wide), so it
    // cannot capture scoped services as fields (DI scope validation rejects it at startup).
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SandboxRegisterProvider> _logger;
    private readonly BlueprintDesignerMetrics _metrics;

    /// <summary>org id → sandbox register id (resolved once, reused thereafter).</summary>
    private readonly ConcurrentDictionary<string, string> _byOrg =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>org id → sandbox-owner wallet address (minted once, reused for re-locking).</summary>
    private readonly ConcurrentDictionary<string, string> _ownerWalletByOrg =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>org id → per-org creation gate, so concurrent starts share one register.</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _orgLocks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Marker carried on the sandbox register's metadata (D1 / T009).</summary>
    public const string SandboxMetadataKey = "sandbox";

    /// <summary>Display name carried on every org's sandbox register.</summary>
    private const string SandboxRegisterName = "Rehearsal Sandbox";

    /// <summary>Algorithm used to mint the per-org sandbox-owner wallet.</summary>
    private const string OwnerWalletAlgorithm = "ED25519";

    /// <summary>Initialises a new instance of the <see cref="SandboxRegisterProvider"/> class.</summary>
    public SandboxRegisterProvider(
        IServiceScopeFactory scopeFactory,
        BlueprintDesignerMetrics metrics,
        ILogger<SandboxRegisterProvider> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<string> GetOrCreateSandboxRegisterAsync(
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            throw new ArgumentException("Organisation id is required.", nameof(organizationId));
        }

        // Fast path — already provisioned and cached.
        if (_byOrg.TryGetValue(organizationId, out var cached))
        {
            _metrics.RecordSandboxProvision("Reused", organizationId);
            return cached;
        }

        var gate = _orgLocks.GetOrAdd(organizationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring the lock — a concurrent caller may have created it.
            if (_byOrg.TryGetValue(organizationId, out cached))
            {
                _metrics.RecordSandboxProvision("Reused", organizationId);
                return cached;
            }

            _logger.LogInformation(
                "Provisioning sandbox register for organisation {OrgId} (devMode, sandbox-tagged, not advertised)",
                organizationId);

            // Scoped clients live for the duration of this creation ceremony only.
            using var scope = _scopeFactory.CreateScope();
            var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();
            var walletClient = scope.ServiceProvider.GetRequiredService<IWalletServiceClient>();

            string resolvedId;
            try
            {
                // Mint (or reuse) the org's stable sandbox-owner wallet — the attestation signer.
                var ownerWalletAddress = await GetOrCreateOwnerWalletAsync(organizationId, walletClient, cancellationToken);

                resolvedId = await RunCreationCeremonyAsync(
                    organizationId, ownerWalletAddress, registerClient, walletClient, cancellationToken);
            }
            catch (Exception)
            {
                _metrics.RecordSandboxProvision("Failed", organizationId);
                _logger.LogInformation(
                    "Sandbox register provisioned: register={RegisterId} outcome={Outcome} org={OrgId}",
                    "—", "Failed", organizationId);
                throw;
            }

            _byOrg[organizationId] = resolvedId;
            _metrics.RecordSandboxProvision("Created", organizationId);

            // T058 — operator-visible audit line.
            _logger.LogInformation(
                "Sandbox register provisioned: register={RegisterId} outcome={Outcome} org={OrgId}",
                resolvedId, "Created", organizationId);

            return resolvedId;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Mints the org's sandbox-owner wallet on first use, caching the address so reuse never
    /// re-mints. Owner = <c>sandbox-owner:{org}</c>, tenant = the org.
    /// </summary>
    private async Task<string> GetOrCreateOwnerWalletAsync(
        string organizationId,
        IWalletServiceClient walletClient,
        CancellationToken cancellationToken)
    {
        if (_ownerWalletByOrg.TryGetValue(organizationId, out var existing))
        {
            return existing;
        }

        var wallet = await walletClient.CreateWalletAsync(
            name: $"sandbox-owner-{organizationId}",
            algorithm: OwnerWalletAlgorithm,
            owner: $"sandbox-owner:{organizationId}",
            tenant: organizationId,
            cancellationToken);

        _ownerWalletByOrg[organizationId] = wallet.Address;
        return wallet.Address;
    }

    /// <summary>
    /// Runs the two-phase owner-attestation ceremony (initiate → sign → finalize) and returns the
    /// created register id. Mirrors the CLI's RegisterCreateCommand signing logic.
    /// </summary>
    private async Task<string> RunCreationCeremonyAsync(
        string organizationId,
        string ownerWalletAddress,
        IRegisterServiceClient registerClient,
        IWalletServiceClient walletClient,
        CancellationToken cancellationToken)
    {
        // Phase 1 — initiate: devMode + sandbox tag + not advertised, owned by the org's wallet.
        var initiateRequest = new InitiateRegisterCreationRequest
        {
            Name = SandboxRegisterName,
            DevMode = true,
            Advertise = false,
            Purpose = RegisterPurpose.General,
            Metadata = new Dictionary<string, string>
            {
                [SandboxMetadataKey] = "true"
            },
            Owners =
            [
                new OwnerInfo
                {
                    UserId = organizationId,
                    WalletId = ownerWalletAddress
                }
            ]
        };

        var initiateResponse = await registerClient.InitiateRegisterCreationAsync(
            initiateRequest, cancellationToken);

        // Phase 2 — sign each attestation hash with the owner wallet (pre-hashed signing), then
        // base64-encode the signature + public key into the signed attestation. This mirrors the
        // CLI flow: DataToSign is a hex SHA-256 hash; convert to bytes and sign with isPreHashed.
        var signedAttestations = new List<SignedAttestation>(initiateResponse.AttestationsToSign.Count);
        foreach (var attestation in initiateResponse.AttestationsToSign)
        {
            var hashBytes = Convert.FromHexString(attestation.DataToSign);

            var signResult = await walletClient.SignTransactionAsync(
                walletAddress: attestation.WalletId,
                transactionData: hashBytes,
                derivationPath: null,
                isPreHashed: true,
                cancellationToken);

            signedAttestations.Add(new SignedAttestation
            {
                AttestationData = attestation.AttestationData,
                PublicKey = Convert.ToBase64String(signResult.PublicKey),
                Signature = Convert.ToBase64String(signResult.Signature),
                Algorithm = ParseAlgorithm(signResult.Algorithm)
            });
        }

        // Phase 3 — finalize: submit the signed attestations; server verifies + seals genesis.
        var finalizeRequest = new FinalizeRegisterCreationRequest
        {
            RegisterId = initiateResponse.RegisterId,
            Nonce = initiateResponse.Nonce,
            SignedAttestations = signedAttestations
        };

        var finalizeResponse = await registerClient.FinalizeRegisterCreationAsync(
            finalizeRequest, cancellationToken);

        return string.IsNullOrWhiteSpace(finalizeResponse.RegisterId)
            ? initiateResponse.RegisterId
            : finalizeResponse.RegisterId;
    }

    /// <summary>
    /// Maps the wallet service's algorithm string onto the register <see cref="SignatureAlgorithm"/>,
    /// defaulting to ED25519 (the sandbox-owner wallet's algorithm) when unrecognised.
    /// </summary>
    private static SignatureAlgorithm ParseAlgorithm(string algorithm) =>
        Enum.TryParse<SignatureAlgorithm>(algorithm, ignoreCase: true, out var parsed)
            ? parsed
            : SignatureAlgorithm.ED25519;
}
