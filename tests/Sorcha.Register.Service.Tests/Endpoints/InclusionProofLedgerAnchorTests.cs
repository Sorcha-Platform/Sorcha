// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// Issue #1372, at the HTTP surface: a Merkle inclusion proof must be anchored to the root the
/// proposing validator sealed, not merely internally consistent.
/// </summary>
/// <remarks>
/// <para>
/// The defect was not that verification returned the wrong answer — it returned a perfectly correct
/// answer to a question nobody meant to ask. The Register Service discarded the sealed root at write
/// time and recomputed one on demand, so a proof generated over altered stored data verified against
/// that recomputation flawlessly. Every check passed and none of them consulted the ledger.
/// </para>
/// <para>
/// These tests exercise both halves against real handlers, because the unit tests over
/// <c>DocketMerkleCommitment</c> cannot see whether the endpoints actually call it —
/// <c>ZKProofIntegrationTests</c> is the cautionary example in this very project: it carries a
/// private copy of the proof-path walk and stayed green while the endpoint it was named after was
/// building its tree from the wrong values entirely.
/// </para>
/// </remarks>
[Collection("RegisterWebApp")]
public class InclusionProofLedgerAnchorTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly RegisterServiceWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _registerId;

    public InclusionProofLedgerAnchorTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _registerId = factory.CreateTestRegisterAsync("Anchor Cross-Check Register", "anchor-xcheck").Result.Id;
    }

    [Fact]
    public async Task AHealthyDocket_YieldsAProofAnchoredToTheRootItSealed()
    {
        var seeded = await SeedDocketAsync(sealWithCorrectRoot: true);

        var proof = await GetProofAsync(seeded.TargetTxId);

        proof.GetProperty("merkleRoot").GetString().Should().Be(seeded.SealedRoot,
            "the proof must be built over the tree the validator committed to");

        var verified = await VerifyAsync(proof, docketNumber: seeded.DocketNumber);
        verified.GetProperty("isValid").GetBoolean().Should().BeTrue();
        verified.GetProperty("ledgerAnchored").GetString().Should().Be("verified");
    }

    [Fact]
    public async Task WithoutADocketNumber_VerificationSaysSo_RatherThanLettingIsValidReadAsLedgerTruth()
    {
        var seeded = await SeedDocketAsync(sealWithCorrectRoot: true);
        var proof = await GetProofAsync(seeded.TargetTxId);

        var verified = await VerifyAsync(proof, docketNumber: null);

        verified.GetProperty("isValid").GetBoolean().Should().BeTrue("the proof path still folds correctly");
        verified.GetProperty("ledgerAnchored").ValueKind.Should().Be(JsonValueKind.Null,
            "a check that was never asked for must not be reported as a pass");
        verified.GetProperty("ledgerAnchorReason").GetString().Should().Contain("no docketNumber",
            "the caller has to be able to tell WHY the anchor is unknown");
    }

    [Fact]
    public async Task AProofThatVerifiesAgainstItsOwnRoot_IsNotAnchoredWhenTheLedgerSealedAnother()
    {
        // The whole shape of #1372 in one test: arithmetic that passes, against a ledger that
        // disagrees. The proof below is genuine — it verifies — but it belongs to a different tree
        // than the docket it is being claimed against.
        var real = await SeedDocketAsync(sealWithCorrectRoot: true);
        var other = await SeedDocketAsync(sealWithCorrectRoot: true);

        var proof = await GetProofAsync(real.TargetTxId);

        var verified = await VerifyAsync(proof, docketNumber: other.DocketNumber);

        verified.GetProperty("ledgerAnchored").GetString().Should().Be("failed");
        verified.GetProperty("ledgerAnchorReason").GetString().Should().Contain("NOT the root");
    }

    [Fact]
    public async Task ADocketWhoseContentsDoNotReproduceItsSealedRoot_RefusesToIssueAProof()
    {
        // A docket recording a commitment its stored transactions no longer produce — the state that
        // altering stored data leaves behind. Before this change the endpoint recomputed a root from
        // the altered contents and returned 200 with a proof that verified perfectly against it.
        var tampered = await SeedDocketAsync(sealWithCorrectRoot: false);

        var response = await _client.GetAsync(
            $"/api/registers/{_registerId}/transactions/{tampered.TargetTxId}/inclusion-proof");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "handing back a proof against a root the ledger never sealed is worse than handing back nothing — "
            + "it verifies, so the caller reports a pass it has no basis for");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(tampered.SealedRoot, "an auditor needs the sealed and recomputed roots side by side");
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private async Task<JsonElement> GetProofAsync(string txId)
    {
        var response = await _client.GetAsync(
            $"/api/registers/{_registerId}/transactions/{txId}/inclusion-proof");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<JsonElement> VerifyAsync(JsonElement proof, long? docketNumber)
    {
        var request = new
        {
            transactionHash = proof.GetProperty("transactionHash").GetString(),
            merkleRoot = proof.GetProperty("merkleRoot").GetString(),
            proofPath = proof.GetProperty("proofPath").EnumerateArray().Select(step => new
            {
                hash = step.GetProperty("hash").GetString(),
                position = step.GetProperty("position").GetInt32()
            }).ToArray(),
            docketNumber
        };

        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_registerId}/inclusion-proofs/verify", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static int _nextDocketNumber;

    private sealed record SeededDocket(long DocketNumber, string TargetTxId, string SealedRoot);

    /// <summary>
    /// Seeds a two-transaction docket. With <paramref name="sealWithCorrectRoot"/> false the docket
    /// records a commitment its own stored transactions do not produce — indistinguishable, from the
    /// service's side, from those transactions having been altered after sealing.
    /// </summary>
    private async Task<SeededDocket> SeedDocketAsync(bool sealWithCorrectRoot)
    {
        // Explicit, distinct docket numbers: the in-memory repository keys dockets by Id within a
        // register and refuses a duplicate, and the cross-docket test needs two on the SAME register
        // (on different registers the anchor would be "not held here", which is a different answer).
        var docketNumber = (ulong)Interlocked.Increment(ref _nextDocketNumber);

        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRegisterRepository>();

        // Whole milliseconds: DocketHasher folds the timestamp in as ToUnixTimeMilliseconds(), so
        // sub-millisecond ticks would make the expected root depend on storage precision.
        var now = new DateTime(
            DateTime.UtcNow.Ticks - (DateTime.UtcNow.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);

        var targetTxId = NewTxId("target");
        var siblingTxId = NewTxId("sibling");
        var targetPayloadHash = NewTxId("target-payload");
        var siblingPayloadHash = NewTxId("sibling-payload");

        var hasher = new DocketHasher(new HashProvider());
        var leaves = new List<string>
        {
            hasher.ComputeTransactionHash(targetTxId, targetPayloadHash, new DateTimeOffset(now, TimeSpan.Zero)),
            hasher.ComputeTransactionHash(siblingTxId, siblingPayloadHash, new DateTimeOffset(now, TimeSpan.Zero)),
        };

        var trueRoot = new MerkleTree(new HashProvider()).ComputeMerkleRoot(leaves);
        var recordedRoot = sealWithCorrectRoot
            ? trueRoot
            : new MerkleTree(new HashProvider()).ComputeMerkleRoot([leaves[1], leaves[0]]);

        var docket = await repository.InsertDocketAsync(new DocketHeader
        {
            Id = docketNumber,
            RegisterId = _registerId,
            TransactionIds = [targetTxId, siblingTxId],
            Hash = NewTxId("dockethash"),
            PreviousHash = string.Empty,
            TimeStamp = now,
            MerkleRoot = recordedRoot
        });

        await repository.InsertTransactionAsync(
            Tx(targetTxId, targetPayloadHash, now, docket.Id));
        await repository.InsertTransactionAsync(
            Tx(siblingTxId, siblingPayloadHash, now, docket.Id));

        return new SeededDocket(checked((long)docket.Id), targetTxId, recordedRoot);
    }

    private TransactionModel Tx(string txId, string payloadHash, DateTime timeStamp, ulong docketNumber) => new()
    {
        RegisterId = _registerId,
        TxId = txId,
        PrevTxId = string.Empty,
        Version = 1,
        SenderWallet = "sender_wallet",
        RecipientsWallets = ["recipient_wallet"],
        TimeStamp = timeStamp,
        PayloadCount = 1,
        Payloads =
        [
            new PayloadModel
            {
                WalletAccess = ["sender_wallet"],
                PayloadSize = 64,
                Hash = payloadHash,
                Data = "data"
            }
        ],
        Signature = "signature",
        DocketNumber = docketNumber
    };

    private static string NewTxId(string seed) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(seed + Guid.NewGuid()))).ToLowerInvariant();
}
