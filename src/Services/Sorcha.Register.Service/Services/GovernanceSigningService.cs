// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Wallet.Contracts.Constants;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Signs a governance control transaction as an <b>organisation on the register's roster</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists (Feature 189 / R-001).</b> Governance control transactions used to be signed
/// by the node's system wallet via <c>ISystemWalletSigningService</c> at
/// <see cref="SorchaDerivationPaths.RegisterControl"/> (slot 101). But a register's governance
/// roster is built from its genesis attestations, which record <i>organisation</i> keys — the node
/// is never on it. The Validator therefore rejected every governance operation on every register
/// whose genesis had sealed, with "submitter not found in roster". Observed live on n1.
/// </para>
/// <para>
/// <b>This does not replace <c>ISystemWalletSigningService</c>.</b> That remains correct for the
/// node's own duties — ingesting genesis, sealing dockets — where the node genuinely is the
/// signing authority. It is wrong only where the authority belongs to an organisation.
/// </para>
/// <para>
/// The signed bytes are <c>SHA-256(UTF-8("{txId}:{payloadHash}"))</c>, signed pre-hashed — byte-for-byte
/// what the Validator recomputes in <c>VerifySignaturesAsync</c>. Diverging here produces a
/// signature that verifies nowhere and a rejection that names neither cause.
/// </para>
/// </remarks>
public interface IGovernanceSigningService
{
    /// <summary>
    /// Signs a governance control transaction with a roster organisation's governance key.
    /// </summary>
    /// <param name="registerId">Register whose roster supplies the authority.</param>
    /// <param name="txId">Transaction id being signed.</param>
    /// <param name="payloadHash">Hex SHA-256 of the canonical payload.</param>
    /// <param name="preferredSubject">
    /// Optional roster subject (<c>did:sorcha:w:{address}</c>) to sign as. When omitted the
    /// register's Owner signs, which is the correct authority for a single-organisation register.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// No roster, no eligible governance member, or a roster subject carrying no wallet address.
    /// </exception>
    Task<GovernanceSignResult> SignAsync(
        string registerId,
        string txId,
        string payloadHash,
        string? preferredSubject = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs an already-computed digest as a roster organisation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used where the signed bytes are not a transaction digest — notably a governance
    /// <b>approval</b>, which commits to
    /// <see cref="GovernanceApprovalStatement"/> rather than to a transaction id and payload hash.
    /// </para>
    /// <para>
    /// Signs at slot 100 for an ordinary organisation. The system register's ceremony-minted Owner
    /// is the one exception — its attestation records the slot-102 docket-signing key (US4).
    /// </para>
    /// </remarks>
    Task<GovernanceSignResult> SignDigestAsync(
        string registerId,
        byte[] digest,
        string? preferredSubject = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of signing as a roster organisation.</summary>
public record GovernanceSignResult
{
    /// <summary>Raw signature bytes.</summary>
    public required byte[] Signature { get; init; }

    /// <summary>Public key of the signing governance key (slot 100).</summary>
    public required byte[] PublicKey { get; init; }

    /// <summary>Signing algorithm identifier.</summary>
    public required string Algorithm { get; init; }

    /// <summary>Wallet address that produced the signature.</summary>
    public required string WalletAddress { get; init; }

    /// <summary>Roster subject the signature is attributable to.</summary>
    public required string Subject { get; init; }
}

/// <inheritdoc />
public class GovernanceSigningService : IGovernanceSigningService
{
    /// <summary>Prefix of a wallet-bearing roster subject.</summary>
    private const string WalletDidPrefix = "did:sorcha:w:";

    /// <summary>
    /// Prefix of the system register's ceremony-minted Owner subject (US4).
    /// </summary>
    /// <remarks>
    /// Deliberately a distinct constant rather than a general "unknown DID method" fallback. This is
    /// one subject, on one register, produced by one offline ceremony, and it stops existing the
    /// moment ownership transfers away from it.
    /// </remarks>
    private const string GenesisDidPrefix = "did:sorcha:genesis:";

    private readonly IGovernanceRosterService _rosterService;
    private readonly IWalletServiceClient _walletClient;
    private readonly SystemWalletSigningOptions _systemWallet;
    private readonly ILogger<GovernanceSigningService> _logger;

    public GovernanceSigningService(
        IGovernanceRosterService rosterService,
        IWalletServiceClient walletClient,
        SystemWalletSigningOptions systemWallet,
        ILogger<GovernanceSigningService> logger)
    {
        _rosterService = rosterService ?? throw new ArgumentNullException(nameof(rosterService));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        // The CONCRETE options, not IOptions<>. AddSystemWalletSigning registers this type as a bare
        // singleton instance (services.AddSingleton(options)), so IOptions<SystemWalletSigningOptions>
        // silently resolves a DEFAULT-constructed one whose ValidatorId is null — and a null validator
        // id makes the Wallet Service hand back the 'default-validator' wallet, which is a real,
        // healthy, entirely wrong wallet. Caught live by the roster-key guard below.
        _systemWallet = systemWallet ?? throw new ArgumentNullException(nameof(systemWallet));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<GovernanceSignResult> SignAsync(
        string registerId,
        string txId,
        string payloadHash,
        string? preferredSubject = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(txId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);

        // Byte-for-byte what the Validator recomputes when verifying a transaction signature.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{txId}:{payloadHash}"));
        return await SignDigestAsync(registerId, digest, preferredSubject, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GovernanceSignResult> SignDigestAsync(
        string registerId,
        byte[] digest,
        string? preferredSubject = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(digest);

        var roster = await _rosterService.GetCurrentRosterAsync(registerId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Register {registerId} has no governance roster, so no organisation can authorise a " +
                "governance change on it. A register created before Feature 189 carries a roster built " +
                "from primary keys and must be recreated to be governable.");

        var attestation = SelectSigner(roster, preferredSubject)
            ?? throw new InvalidOperationException(
                $"Register {registerId} has no roster member with a governance role (Owner or Admin)" +
                (preferredSubject is null ? "." : $" matching subject '{preferredSubject}'."));

        // US4 - the system register's ceremony-minted Owner. Its subject carries no wallet address, so
        // the ordinary path below could not reach a key and the SSR was ungovernable: a propose
        // returned "carries no wallet address ... so there is no key to sign with".
        //
        // The obstacle was only ever the SUBJECT's shape. The ceremony derives every key from one
        // BIP39 mnemonic, the Owner attestation records the slot-102 sorcha:docket-signing key, and
        // `sorcha system-register import-validator-key` recovers that mnemonic into a system wallet -
        // so this node already holds the key, and it is the same wallet the validator seals dockets
        // with. Nothing needed re-minting; there was simply no path that reached it.
        var (walletAddress, derivationPath) = IsGenesisSubject(attestation.Subject)
            ? (await _walletClient.CreateOrRetrieveSystemWalletAsync(_systemWallet.ValidatorId, cancellationToken),
               SorchaDerivationPaths.DocketSigning)
            : (ExtractWalletAddress(attestation.Subject)
                   ?? throw new InvalidOperationException(
                       $"Roster subject '{attestation.Subject}' on register {registerId} carries no wallet " +
                       $"address (expected '{WalletDidPrefix}{{address}}'), so there is no key to sign with."),
               // Slot 100 - the organisation's governance key, and the key its roster attestation
               // records. Signing with the wallet's primary key here reproduces the original defect.
               SorchaDerivationPaths.RegisterAttestation);

        var signResult = await _walletClient.SignTransactionAsync(
            walletAddress,
            digest,
            derivationPath,
            isPreHashed: true,
            cancellationToken);

        // The signature is worthless unless its key is the one the roster records — the Validator
        // checks exactly that, and a mismatch surfaces as an opaque authorisation failure a long way
        // from the cause. Checked on the GENESIS path specifically, because that is the one where the
        // wallet is resolved from CONFIGURATION rather than from the subject itself: a misconfigured
        // SystemWalletSigning:ValidatorId is the single thing that can silently point this at a
        // different, perfectly valid wallet.
        //
        // Deliberately not applied to the wallet-DID path. There the address is derived from the
        // subject, so it cannot drift the same way, and making this a blanket check would be a
        // behaviour change to every governance signature on the platform — worth considering, but on
        // its own evidence rather than smuggled in beside a US4 fix.
        if (IsGenesisSubject(attestation.Subject)
            && !GovernanceKeyMatcher.Matches(attestation.PublicKey, Convert.ToBase64String(signResult.PublicKey)))
        {
            throw new InvalidOperationException(
                $"Signed as '{attestation.Subject}' on register {registerId} using wallet {walletAddress} " +
                $"at {derivationPath}, but the resulting key is not the one the roster records for that " +
                "subject, so the Validator would refuse it. For the system register this almost always " +
                "means SystemWalletSigning:ValidatorId does not match the validator whose key the " +
                "genesis ceremony minted.");
        }

        _logger.LogInformation(
            "Governance digest on register {RegisterId} signed as {Subject} (role {Role}) using wallet {Wallet} at {Path}",
            registerId, attestation.Subject, attestation.Role, walletAddress, derivationPath);

        return new GovernanceSignResult
        {
            Signature = signResult.Signature,
            PublicKey = signResult.PublicKey,
            Algorithm = signResult.Algorithm,
            WalletAddress = walletAddress,
            Subject = attestation.Subject
        };
    }

    /// <summary>
    /// Chooses the roster member to sign as: the requested subject when given, otherwise the Owner,
    /// falling back to any Admin.
    /// </summary>
    /// <remarks>
    /// Defaulting to the Owner is correct for a single-organisation register, which is the case
    /// User Story 1 covers. Consortium registers pass <c>preferredSubject</c> so each organisation
    /// signs its own approval — one authority per signature, never one node signing for everyone.
    /// </remarks>
    /// <summary>
    /// Whether this is the system register's ceremony-minted Owner (US4).
    /// </summary>
    /// <remarks>
    /// Expected to become dead code: the first governance act on the system register transfers
    /// ownership to a real <c>did:sorcha:w:</c> organisation, after which this subject is off the
    /// roster and nothing reaches this branch again. Kept keyed on the prefix rather than generalised
    /// into "governance may sign at any derivation path", which would let a future subject quietly
    /// sign with a key its attestation does not record.
    /// </remarks>
    private static bool IsGenesisSubject(string? subject)
        => subject?.StartsWith(GenesisDidPrefix, StringComparison.Ordinal) == true;

    private static RegisterAttestation? SelectSigner(AdminRoster roster, string? preferredSubject)
    {
        var eligible = roster.ControlRecord.Attestations
            .Where(a => a.Role is RegisterRole.Owner or RegisterRole.Admin)
            .ToList();

        if (!string.IsNullOrWhiteSpace(preferredSubject))
        {
            return eligible.FirstOrDefault(
                a => string.Equals(a.Subject, preferredSubject, StringComparison.Ordinal));
        }

        return eligible.FirstOrDefault(a => a.Role == RegisterRole.Owner)
               ?? eligible.FirstOrDefault();
    }

    /// <summary>
    /// Extracts the wallet address from a <c>did:sorcha:w:{address}</c> roster subject, or null when
    /// the subject is not wallet-bearing.
    /// </summary>
    private static string? ExtractWalletAddress(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject) ||
            !subject.StartsWith(WalletDidPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var address = subject[WalletDidPrefix.Length..];
        return string.IsNullOrWhiteSpace(address) ? null : address;
    }
}
