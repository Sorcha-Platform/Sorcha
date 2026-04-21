// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Models.LocalRelationship;

namespace Sorcha.Register.Core.LocalRelationship;

/// <summary>
/// Default <see cref="IRegisterLocalRelationshipService"/> implementation (Feature 108).
/// Reads the genesis control transaction (+ any subsequent governance control transactions)
/// and derives the role set from the attestations and validator roster contained within.
/// </summary>
/// <remarks>
/// v1 derives from the genesis control record only. Governance ops (AddValidator/RemoveValidator
/// /RotateKey) that produce later control transactions are detected (cache invalidation fires
/// on control-tx seal) but their payload merging into the roster is a later enhancement —
/// currently we re-read the genesis record on invalidation. This is sufficient for PingPongN1
/// and for any deployment that sets the roster at creation time and does not later mutate it.
/// </remarks>
public sealed class RegisterLocalRelationshipService : IRegisterLocalRelationshipService
{
    private readonly IReadOnlyRegisterRepository _repository;
    private readonly ILocalIdentityProvider _identityProvider;
    private readonly ILogger<RegisterLocalRelationshipService>? _logger;

    private readonly ConcurrentDictionary<string, RegisterLocalRelationship> _cache
        = new(StringComparer.Ordinal);

    public RegisterLocalRelationshipService(
        IReadOnlyRegisterRepository repository,
        ILocalIdentityProvider identityProvider,
        ILogger<RegisterLocalRelationshipService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RegisterLocalRelationship?> DeriveAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        if (_cache.TryGetValue(registerId, out var cached))
            return cached;

        var identity = await _identityProvider.GetAsync(cancellationToken);
        var fresh = await ComputeAsync(registerId, identity, cancellationToken);
        if (fresh is not null)
            _cache[registerId] = fresh;
        return fresh;
    }

    /// <inheritdoc />
    public void Invalidate(string registerId)
    {
        if (_cache.TryRemove(registerId, out _))
        {
            _logger?.LogDebug(
                "Invalidated RegisterLocalRelationship cache for register {RegisterId}",
                registerId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegisterLocalRelationship>> DeriveAllAsync(
        byte[]? validatorPublicKeyOverride = null,
        CancellationToken cancellationToken = default)
    {
        var registers = (await _repository.GetRegistersAsync(cancellationToken)).ToList();
        var identity = await _identityProvider.GetAsync(cancellationToken);
        if (validatorPublicKeyOverride is { Length: > 0 })
        {
            identity = identity with { ValidatorPublicKey = validatorPublicKeyOverride };
        }

        // DIAG (F108 bootstrap probe — remove once fix confirmed)
        _logger?.LogInformation(
            "DIAG DeriveAllAsync: {RegisterCount} register(s) in repository; override={HasOverride}, walletAddresses={WalletCount}",
            registers.Count,
            validatorPublicKeyOverride is { Length: > 0 },
            identity.WalletAddresses.Count);

        var results = new List<RegisterLocalRelationship>();
        foreach (var register in registers)
        {
            var derived = await ComputeAsync(register.Id, identity, cancellationToken);
            if (derived is not null)
                results.Add(derived);
        }

        // DIAG: how many relationships came back with IsValidator=true?
        var validatorCount = results.Count(r => r.IsValidator);
        _logger?.LogInformation(
            "DIAG DeriveAllAsync: {NonNullCount} non-null relationship(s), {ValidatorCount} with IsValidator=true",
            results.Count, validatorCount);
        return results;
    }

    private async Task<RegisterLocalRelationship?> ComputeAsync(
        string registerId,
        LocalIdentitySnapshot identity,
        CancellationToken cancellationToken)
    {
        // Primary path: read genesis docket and find its Control transaction.
        var genesis = await _repository.GetDocketAsync(registerId, 0, cancellationToken);
        if (genesis is not null)
        {
            var genesisTxs = await _repository.GetTransactionsByDocketAsync(registerId, 0, cancellationToken);
            var docketControlRecord = TryExtractControlRecord(registerId, genesisTxs);
            if (docketControlRecord is null)
            {
                // No control record — legacy register. Fall back to None (subscriber).
                return new RegisterLocalRelationship(
                    RegisterId: registerId,
                    Roles: RegisterRoleSet.None,
                    ControlRecordVersion: 0,
                    DerivedAt: DateTimeOffset.UtcNow);
            }

            var docketRoles = DeriveRoles(docketControlRecord, identity);
            var docketVersion = (int)(genesis.Id);

            _logger?.LogDebug(
                "Derived relationship for register {RegisterId}: {Roles} (controlRecordVersion={Version}, source=docket)",
                registerId, docketRoles, docketVersion);

            return new RegisterLocalRelationship(
                RegisterId: registerId,
                Roles: docketRoles,
                ControlRecordVersion: docketVersion,
                DerivedAt: DateTimeOffset.UtcNow);
        }

        // Pre-seal fallback: genesis docket not yet written, but the register row may carry
        // the stashed InitialControlRecord from FinalizeAsync. This lets the validator enrol
        // for monitoring before docket 0 exists and breaks the bootstrap deadlock where
        // the validator can't seal the genesis docket without first seeing a control record,
        // which can't exist without the genesis docket being sealed.
        var registerRow = await _repository.GetRegisterAsync(registerId, cancellationToken);
        // DIAG (F108 bootstrap probe — remove once fix confirmed)
        _logger?.LogInformation(
            "DIAG ComputeAsync stash path for {RegisterId}: registerRowFound={RowFound}, InitialControlRecord={HasStash}, rosterEntries={RosterCount}",
            registerId,
            registerRow is not null,
            registerRow?.InitialControlRecord is not null,
            registerRow?.InitialControlRecord?.Validators?.Validators.Count ?? 0);
        if (registerRow?.InitialControlRecord is not null)
        {
            var stashRoles = DeriveRoles(registerRow.InitialControlRecord, identity);
            _logger?.LogInformation(
                "DIAG ComputeAsync stash path for {RegisterId}: derived roles={Roles} (source=stash)",
                registerId, stashRoles);

            return new RegisterLocalRelationship(
                RegisterId: registerId,
                Roles: stashRoles,
                ControlRecordVersion: 0,
                DerivedAt: DateTimeOffset.UtcNow);
        }

        _logger?.LogDebug(
            "Cannot derive relationship for register {RegisterId}: genesis docket not present locally and no stash available",
            registerId);
        return null;
    }

    private RegisterControlRecord? TryExtractControlRecord(
        string registerId,
        IEnumerable<TransactionModel> genesisTransactions)
    {
        foreach (var tx in genesisTransactions)
        {
            if (tx?.MetaData?.TransactionType != TransactionType.Control) continue;
            if (tx.Payloads is null || tx.Payloads.Length == 0) continue;

            var data = tx.Payloads[0].Data;
            if (string.IsNullOrEmpty(data)) continue;

            try
            {
                var bytes = DecodeBase64Auto(data);
                var record = JsonSerializer.Deserialize<RegisterControlRecord>(bytes);
                if (record is not null) return record;
            }
            catch (FormatException ex)
            {
                _logger?.LogDebug(ex,
                    "Control tx payload for register {RegisterId} is not valid Base64/Base64Url", registerId);
            }
            catch (JsonException ex)
            {
                _logger?.LogDebug(ex,
                    "Control tx payload for register {RegisterId} is not a valid RegisterControlRecord", registerId);
            }
        }
        return null;
    }

    private RegisterRoleSet DeriveRoles(RegisterControlRecord controlRecord, LocalIdentitySnapshot identity)
    {
        var roles = RegisterRoleSet.None;

        // Attestations — match by Subject DID referencing a local wallet address.
        if (identity.WalletAddresses.Count > 0 && controlRecord.Attestations.Count > 0)
        {
            foreach (var attestation in controlRecord.Attestations)
            {
                if (!SubjectMatchesLocalWallet(attestation.Subject, identity.WalletAddresses)) continue;

                roles |= attestation.Role switch
                {
                    RegisterRole.Owner    => RegisterRoleSet.Owner,
                    RegisterRole.Admin    => RegisterRoleSet.Admin,
                    RegisterRole.Auditor  => RegisterRoleSet.Auditor,
                    RegisterRole.Designer => RegisterRoleSet.Designer,
                    _ => RegisterRoleSet.None
                };
            }
        }

        // Validator roster match.
        if (identity.ValidatorPublicKey is { Length: > 0 } validatorKey &&
            controlRecord.Validators is { } roster)
        {
            // DIAG (F108 bootstrap probe — remove once fix confirmed)
            var overrideB64 = Convert.ToBase64String(validatorKey);
            _logger?.LogInformation(
                "DIAG DeriveRoles roster compare: override={Override} ({Bytes}B) vs {EntryCount} roster entries",
                overrideB64, validatorKey.Length, roster.Validators.Count);
            foreach (var entry in roster.Validators)
            {
                if (string.IsNullOrEmpty(entry.PublicKey)) continue;

                byte[] entryKey;
                try
                {
                    entryKey = Convert.FromBase64String(entry.PublicKey);
                }
                catch (FormatException)
                {
                    _logger?.LogInformation(
                        "DIAG DeriveRoles: roster entry PublicKey={Key} failed base64 decode",
                        entry.PublicKey);
                    continue;
                }

                var match = entryKey.AsSpan().SequenceEqual(validatorKey);
                _logger?.LogInformation(
                    "DIAG DeriveRoles: entry PublicKey={Entry} ({EntryBytes}B) match={Match}",
                    entry.PublicKey, entryKey.Length, match);
                if (match)
                {
                    roles |= RegisterRoleSet.Validator;
                    break;
                }
            }
        }
        else
        {
            _logger?.LogInformation(
                "DIAG DeriveRoles: skipping validator match — identityKey={HasKey}, roster={HasRoster}",
                identity.ValidatorPublicKey is { Length: > 0 },
                controlRecord.Validators is not null);
        }

        return roles;
    }

    /// <summary>
    /// Attestation subjects are DIDs like <c>did:sorcha:wallet:{address}</c> or
    /// <c>did:sorcha:org:{address}</c>. Match by the address segment.
    /// </summary>
    private static bool SubjectMatchesLocalWallet(string subject, IReadOnlyCollection<string> walletAddresses)
    {
        if (string.IsNullOrEmpty(subject)) return false;

        // Fast path — direct match (some subjects are bare addresses).
        if (walletAddresses.Contains(subject)) return true;

        // DID shape: did:{method}:{kind}:{address}
        var parts = subject.Split(':');
        if (parts.Length < 3) return false;
        var tail = parts[^1];
        return walletAddresses.Contains(tail);
    }

    private static byte[] DecodeBase64Auto(string encoded)
    {
        var s = encoded.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
