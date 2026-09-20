// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.Register.Models;
using Sorcha.Register.Service.Services;
using Sorcha.Register.Service.Services.Implementation;

using Xunit;

namespace Sorcha.Register.Service.Tests;

/// <summary>
/// #1667 — published participant records must survive a register-service restart.
/// </summary>
/// <remarks>
/// <para>
/// The index is in-memory and was populated only by live docket ingest, so a restart emptied it and
/// nothing rebuilt it. The records stayed sealed on the ledger while nothing served them, silently
/// unbinding every role on every register. Observed on n1 on 2026-09-19 after an ordinary code-only
/// deploy: the list endpoint returned <c>total: 0</c> and resolve returned 404 for two records whose
/// transactions were still on the chain.
/// </para>
/// <para>
/// These tests drive the replay directly, which is the part that has to be right. A fresh index —
/// one that has never seen an ingest — must resolve what the ledger holds.
/// </para>
/// </remarks>
public class ParticipantIndexStartupRebuildTests
{
    private const string RegisterId = "4145b4e7102246ae997f2510acba2220";

    /// <summary>One record's identity across its versions.</summary>
    private const string Same = "c5aff551-03cc-482e-9562-72a24b54dec7";

    [Fact]
    public void Replay_RestoresRecordsIntoAnIndexThatNeverSawThemIngested()
    {
        var index = new ParticipantIndexService(NullLogger<ParticipantIndexService>.Instance);
        // The state after a restart: nothing at all.
        index.Resolve(RegisterId, "recipient").Should().BeNull();

        var (indexed, failed) = ParticipantIndexStartupRebuildService.Replay(
            index, RegisterId,
            [
                ParticipantTx("tx-provider-1", "provider", "Cold-start Run 5 Provider", "Active", 1, "ws11qqzq-personal"),
                ParticipantTx("tx-recipient-1", "recipient", "Cold-start Run 5 Recipient", "Active", 1, "ws11qzzr-org"),
            ],
            NullLogger.Instance);

        indexed.Should().Be(2);
        failed.Should().Be(0);

        var recipient = index.Resolve(RegisterId, "recipient");
        recipient.Should().NotBeNull();
        recipient!.Addresses.Should().ContainSingle()
            .Which.WalletAddress.Should().Be("ws11qzzr-org");
        index.Resolve(RegisterId, "provider").Should().NotBeNull();
    }

    [Fact]
    public void Replay_AppliesInTheOrderGiven_SoALaterVersionWins()
    {
        // The rebuild reads oldest-first precisely so this holds. Replaying newest-first would
        // resurrect a superseded or revoked binding, which is worse than serving nothing.
        var index = new ParticipantIndexService(NullLogger<ParticipantIndexService>.Instance);

        ParticipantIndexStartupRebuildService.Replay(
            index, RegisterId,
            [
                ParticipantTx("tx-1", "recipient", "Recipient Org", "Active", 1, "ws11q-first", Same),
                ParticipantTx("tx-2", "recipient", "Recipient Org", "Active", 2, "ws11q-second", Same),
            ],
            NullLogger.Instance);

        index.Resolve(RegisterId, "recipient")!.Addresses[0].WalletAddress.Should().Be("ws11q-second");
    }

    [Fact]
    public void TheRebuildReadsTheLedgerOldestFirst()
    {
        // Replay applies records in the order it receives them, so the sort the service asks for is
        // what makes the test above hold in production. Flipping it compiles, passes every other
        // test, and silently restores superseded or revoked bindings — so the value is pinned here.
        ParticipantIndexStartupRebuildService.ReplayOrder
            .Should().Be(Sorcha.Register.Models.Enums.TransactionSort.TimeStampAscending);
    }

    [Fact]
    public void Replay_OneUnreadablePayload_DoesNotCostTheRegisterItsOtherRecords()
    {
        var index = new ParticipantIndexService(NullLogger<ParticipantIndexService>.Instance);

        var (indexed, failed) = ParticipantIndexStartupRebuildService.Replay(
            index, RegisterId,
            [
                Tx("tx-bad", "this is not base64 json at all"),
                ParticipantTx("tx-good", "recipient", "Recipient Org", "Active", 1, "ws11q-good"),
            ],
            NullLogger.Instance);

        indexed.Should().Be(1);
        failed.Should().Be(1);
        index.Resolve(RegisterId, "recipient").Should().NotBeNull();
    }

    [Fact]
    public void Replay_ATransactionWithNoPayload_IsSkippedRatherThanThrowing()
    {
        var index = new ParticipantIndexService(NullLogger<ParticipantIndexService>.Instance);

        var (indexed, failed) = ParticipantIndexStartupRebuildService.Replay(
            index, RegisterId, [new TransactionModel { TxId = "tx-empty", Payloads = [] }], NullLogger.Instance);

        indexed.Should().Be(0);
        failed.Should().Be(1);
    }

    private static TransactionModel ParticipantTx(
        string txId, string participantName, string organizationName, string status, int version,
        string walletAddress, string? participantId = null)
    {
        var payload = new
        {
            // Defaults to a fresh id, but a version bump of the SAME record must pass the SAME id:
            // two ids sharing a name are two participants, and which one resolves is then
            // arbitrary. That made the ordering test below pass locally and fail in CI.
            participantId = participantId ?? Guid.NewGuid().ToString(),
            participantName,
            organizationName,
            status,
            version,
            addresses = new[]
            {
                new { walletAddress, publicKey = "pk-" + walletAddress, algorithm = "ED25519", primary = true }
            }
        };

        return Tx(txId, JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    private static TransactionModel Tx(string txId, string rawPayload) => new()
    {
        TxId = txId,
        TimeStamp = DateTime.UtcNow,
        Payloads = [new PayloadModel { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawPayload)) }],
    };
}
