// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// Regression guard for the P1 (2026-07-04): new-register genesis sealing broke because the internal
/// validator->register docket-write endpoint (POST /api/registers/{id}/dockets) carried
/// <c>.WithRequestValidation()</c>. That seam recurses into <c>WriteDocketRequest.Transactions</c> and
/// runs user-input DataAnnotations over the embedded ledger transactions — and a GENESIS control tx has
/// an empty <c>PrevTxId</c> (nothing precedes genesis), which fails <c>TransactionModel</c>'s
/// <c>[StringLength(64, MinimumLength = 64)]</c> → 400 → the genesis docket never persists.
///
/// The fix removed request-validation from this internal, already-crypto-verified machine endpoint. This
/// test asserts the endpoint no longer rejects a genesis docket whose control tx has an empty PrevTxId
/// (i.e. NOT a validation 400), so the seam can't be silently re-added.
/// </summary>
[Collection("RegisterWebApp")]
public class DocketWriteGenesisValidationTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DocketWriteGenesisValidationTests(RegisterServiceWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task WriteDocket_GenesisDocketWithEmptyPrevTxIdControlTx_IsNotRejectedByValidation()
    {
        // A fresh register id → the genesis (DocketNumber 0) create-on-sync path.
        var registerId = "genval" + Guid.NewGuid().ToString("N");

        // The genesis control transaction: PrevTxId is empty because nothing precedes genesis. This is
        // the exact shape that tripped TransactionModel.[StringLength(64, MinimumLength = 64)] under the
        // recursive request-validation seam.
        var genesisTx = new TransactionModel
        {
            TxId = new string('a', 64),
            PrevTxId = string.Empty,
            RegisterId = registerId,
            SenderWallet = "test-wallet",
            TimeStamp = DateTime.UtcNow,
            Signature = "test-signature",
            Payloads = Array.Empty<PayloadModel>(),
            MetaData = new TransactionMetaData
            {
                RegisterId = registerId,
                TransactionType = TransactionType.Control,
            },
        };

        var request = new
        {
            DocketId = new string('b', 64),
            DocketNumber = 0L,
            PreviousHash = (string?)null,
            DocketHash = new string('b', 64),
            CreatedAt = DateTimeOffset.UtcNow,
            TransactionIds = new[] { genesisTx.TxId },
            ProposerValidatorId = "test-validator",
            MerkleRoot = new string('c', 64),
            Transactions = new[] { genesisTx },
        };

        var response = await _client.PostAsJsonAsync($"/api/registers/{registerId}/dockets", request);

        // The key regression assertion: the recursive user-input validation reject is gone. A downstream
        // business outcome (created / conflict / etc.) is fine — a 400 BadRequest is NOT, because that is
        // the validation failure this fix removed.
        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest,
            "an internal docket write of a genesis docket (control tx with empty PrevTxId) must not be " +
            "rejected by user-input request validation");
    }
}
