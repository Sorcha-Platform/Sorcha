// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services;

namespace Sorcha.Register.Service.Services.Implementation;

/// <summary>
/// Rebuilds <see cref="ParticipantIndexService"/> from the ledger once at startup, so published
/// participant records survive a restart (#1667).
/// </summary>
/// <remarks>
/// <para>
/// Why this exists: the index is an in-memory singleton whose only population path is the
/// docket-ingest hook, which fires for transactions observed <em>live</em>. A restart therefore
/// emptied it, and nothing rebuilt it. The records stayed sealed on the ledger while nothing served
/// them, so every role on every register silently became unbound — taking recipient-key resolution,
/// the published-record tier of sender-wallet resolution, and <c>VAL_BP_002</c>'s published-record
/// tier down with it. Observed on n1 on 2026-09-19 after an ordinary code-only deploy.
/// </para>
/// <para>
/// The ledger is the source of truth, deliberately. The index also writes through to Redis, but that
/// carries a TTL and the cache is optional, so a cache read cannot be the recovery path — it would
/// restore a register on one node and not another, which is worse than restoring neither.
/// </para>
/// <para>
/// Transactions are replayed oldest-first so that later versions of a record overwrite earlier ones,
/// exactly as live ingest would have applied them.
/// </para>
/// </remarks>
public sealed class ParticipantIndexStartupRebuildService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ParticipantIndexService _index;
    private readonly ILogger<ParticipantIndexStartupRebuildService> _logger;

    /// <summary>Matches the bloom rebuild: let Mongo/Redis finish coming up first.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Oldest-first, and load-bearing: the replay applies records in the order it reads them, so a
    /// later version must arrive after the earlier one it supersedes. Reading newest-first would
    /// resurrect superseded — or revoked — bindings, which is worse than serving none. Pinned by test.
    /// </summary>
    internal const TransactionSort ReplayOrder = TransactionSort.TimeStampAscending;

    /// <summary>
    /// Initialises a new instance of <see cref="ParticipantIndexStartupRebuildService"/>.
    /// </summary>
    public ParticipantIndexStartupRebuildService(
        IServiceProvider services,
        ParticipantIndexService index,
        ILogger<ParticipantIndexStartupRebuildService> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (stoppingToken.IsCancellationRequested) return;

        await using var scope = _services.CreateAsyncScope();
        var registerManager = scope.ServiceProvider.GetRequiredService<RegisterManager>();
        var transactionManager = scope.ServiceProvider.GetRequiredService<TransactionManager>();

        IReadOnlyList<Sorcha.Register.Models.Register> registers;
        try
        {
            registers = (await registerManager.GetAllRegistersAsync(stoppingToken)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Participant index rebuild: failed to enumerate registers; skipping.");
            return;
        }

        var indexed = 0;
        var failed = 0;
        var registersWithRecords = 0;

        foreach (var register in registers)
        {
            if (stoppingToken.IsCancellationRequested) break;

            var registerId = register.Id;
            if (string.IsNullOrEmpty(registerId)) continue;

            try
            {
                var participantTxs = await transactionManager.GetTransactionsByTypeAsync(
                    registerId,
                    TransactionType.Participant,
                    ReplayOrder,
                    cancellationToken: stoppingToken);

                var (countForRegister, failures) = Replay(_index, registerId, participantTxs, _logger);
                failed += failures;

                if (countForRegister > 0)
                {
                    registersWithRecords++;
                    indexed += countForRegister;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex,
                    "Participant index rebuild: failed to replay register {RegisterId}; continuing.",
                    registerId);
            }
        }

        _logger.LogInformation(
            "Participant index rebuild complete: {Indexed} record(s) across {Registers} register(s) "
            + "(of {Total} scanned), {Failed} failed.",
            indexed, registersWithRecords, registers.Count, failed);
    }

    /// <summary>
    /// Replays a register's participant transactions into the index, in the order given.
    /// </summary>
    /// <remarks>
    /// Internal so the recovery semantics can be tested directly, without standing up a host: that a
    /// fresh index resolves records it never saw ingested, that a later version wins over an earlier
    /// one, and that one malformed payload does not abort the rest of the register.
    /// </remarks>
    /// <returns>How many transactions were indexed, and how many were skipped.</returns>
    internal static (int Indexed, int Failed) Replay(
        ParticipantIndexService index,
        string registerId,
        IEnumerable<Sorcha.Register.Models.TransactionModel> transactions,
        ILogger logger)
    {
        var indexed = 0;
        var failed = 0;

        foreach (var tx in transactions)
        {
            if (tx.Payloads.Length == 0 || string.IsNullOrEmpty(tx.Payloads[0].Data))
            {
                failed++;
                continue;
            }

            try
            {
                var payloadJson = System.Text.Encoding.UTF8.GetString(
                    Sorcha.TransactionHandler.Services.ContentEncodings.DecodeBase64Auto(tx.Payloads[0].Data));
                var payloadElement = System.Text.Json.JsonSerializer
                    .Deserialize<System.Text.Json.JsonElement>(payloadJson);
                index.IndexParticipant(registerId, tx.TxId, payloadElement, tx.TimeStamp);
                indexed++;
            }
            catch (Exception ex)
            {
                // One bad payload must not cost the register every other record it holds.
                failed++;
                logger.LogWarning(ex,
                    "Participant index rebuild: failed to replay participant TX {TxId} on {RegisterId}.",
                    tx.TxId, registerId);
            }
        }

        return (indexed, failed);
    }
}
