// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Transaction = Sorcha.Validator.Service.Models.Transaction;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Pinning tests for <see cref="DocketRegisterProjection"/>'s SenderWallet derivation. Wave 12 fix:
/// the persisted <c>TransactionModel.SenderWallet</c> must use the bech32 wallet address from
/// <see cref="Signature.SignedBy"/> when available, falling back to base64url(PublicKey) only as a
/// last resort.
/// </summary>
/// <remarks>
/// Ported from <c>Sorcha.Validator.Service.IntegrationTests.DocketSerializerSenderWalletTests</c>
/// during Feature 187 (#1370), which collapsed the two competing docket→register projections into
/// <see cref="DocketRegisterProjection"/> and deleted <c>DocketSerializer.ToRegisterModel</c>. The
/// original file recorded that it lived in the IntegrationTests project only because this unit-test
/// project had unrelated compile errors at the time, and asked to be moved back once that was fixed —
/// it is, so this is that move. These are pure unit tests; they need no fixture or host.
/// </remarks>
public class DocketRegisterProjectionSenderWalletTests
{
    private const string Bech32Address =
        "ws11qpgd645h52t6lwf74awwkc7szvtjrwjsf4qv0rprw4lms4tje2tmsqgl4pv";

    [Fact]
    public void ToDocketModel_SignatureWithSignedBy_UsesBech32Address()
    {
        // Wave 12 fix: when Signature.SignedBy is populated (the normal signing path via Wallet
        // Service), the projection must persist that bech32 address to TransactionModel.SenderWallet
        // so the /api/register/query/wallets/{address}/transactions lookup matches.
        var docket = CreateDocketWithTransaction("tx-bech32", signedBy: Bech32Address);

        var model = DocketRegisterProjection.ToDocketModel(docket);

        model.Transactions.Should().ContainSingle();
        model.Transactions[0].SenderWallet.Should().Be(Bech32Address);
    }

    [Fact]
    public void ToDocketModel_SignatureWithoutSignedBy_FallsBackToBase64UrlPublicKey()
    {
        // Legacy fallback path: when SignedBy is missing (e.g. genesis transactions with no Wallet
        // Service round-trip), the persisted SenderWallet should still be non-empty — derived from the
        // raw public key as base64url. It will not match a bech32 wallet lookup, but it preserves a
        // deterministic, queryable identity.
        var docket = CreateDocketWithTransaction("tx-no-signedby", signedBy: null);

        var model = DocketRegisterProjection.ToDocketModel(docket);

        model.Transactions.Should().ContainSingle();
        model.Transactions[0].SenderWallet.Should().NotBeNullOrEmpty();
        model.Transactions[0].SenderWallet.Should().NotStartWith("ws1",
            "the fallback path uses base64url(PublicKey), not bech32 encoding");
    }

    [Fact]
    public void ToDocketModel_SignatureWithWhitespaceSignedBy_FallsBackToBase64UrlPublicKey()
    {
        // Whitespace-only SignedBy should be treated as missing and fall through to the base64url
        // fallback. Pins the IsNullOrWhiteSpace behaviour so a refactor doesn't regress to a Length>0
        // check (which would happily emit "   " as a wallet address).
        var docket = CreateDocketWithTransaction("tx-whitespace", signedBy: "   ");

        var model = DocketRegisterProjection.ToDocketModel(docket);

        model.Transactions[0].SenderWallet.Should().NotBe("   ");
        model.Transactions[0].SenderWallet.Should().NotStartWith("ws1");
    }

    [Fact]
    public void ToDocketModel_NoSignatures_UsesSystemSender()
    {
        var docket = CreateDocketWithTransaction("tx-unsigned", signedBy: null);
        docket.Transactions[0].Signatures.Clear();

        var model = DocketRegisterProjection.ToDocketModel(docket);

        model.Transactions[0].SenderWallet.Should().Be("system");
    }

    private static Docket CreateDocketWithTransaction(string transactionId, string? signedBy)
    {
        return new Docket
        {
            DocketId = "test-docket",
            DocketHash = "hash",
            PreviousHash = "prev",
            DocketNumber = 1,
            RegisterId = "register-1",
            CreatedAt = DateTimeOffset.UtcNow,
            ProposerValidatorId = "validator-1",
            MerkleRoot = "merkle",
            Status = DocketStatus.Confirmed,
            ProposerSignature = new RegisterSignature
            {
                PublicKey = [1, 2, 3],
                SignatureValue = [4, 5, 6],
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            },
            Transactions =
            [
                new Transaction
                {
                    TransactionId = transactionId,
                    RegisterId = "register-1",
                    BlueprintId = "blueprint-1",
                    ActionId = "1",
                    Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
                    PayloadHash = $"hash-{transactionId}",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Priority = TransactionPriority.Normal,
                    Signatures =
                    [
                        new RegisterSignature
                        {
                            PublicKey = [1, 2, 3],
                            SignatureValue = [4, 5, 6],
                            Algorithm = "ED25519",
                            SignedAt = DateTimeOffset.UtcNow,
                            SignedBy = signedBy
                        }
                    ],
                    Metadata = new Dictionary<string, string>()
                }
            ]
        };
    }
}
