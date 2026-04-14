// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Text.Json;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.IntegrationTests;

/// <summary>
/// Pinning tests for <see cref="DocketSerializer.ToRegisterModel"/>'s
/// SenderWallet derivation. Wave 12 fix: the persisted
/// <c>TransactionModel.SenderWallet</c> must use the bech32 wallet address
/// from <see cref="Signature.SignedBy"/> when available, falling back to
/// base64url(PublicKey) only as a last resort.
/// </summary>
/// <remarks>
/// <para>
/// These tests live in the IntegrationTests project (rather than
/// <c>Sorcha.Validator.Service.Tests</c>) because the parent unit-test
/// project has pre-existing compile errors unrelated to wave 12 — see
/// the wave 12 PR description for the maintenance backlog. Once that
/// project is unblocked these can move alongside the rest of the
/// DocketSerializer tests.
/// </para>
/// <para>
/// The tests themselves are pure unit tests against
/// <see cref="DocketSerializer"/> and don't require any test fixture
/// or service host.
/// </para>
/// </remarks>
public class DocketSerializerSenderWalletTests
{
    private const string Bech32Address =
        "ws11qpgd645h52t6lwf74awwkc7szvtjrwjsf4qv0rprw4lms4tje2tmsqgl4pv";

    [Fact]
    public void ToRegisterModel_SignatureWithSignedBy_UsesBech32Address()
    {
        // Wave 12 fix: when Signature.SignedBy is populated (the normal
        // signing path via Wallet Service), DocketSerializer must persist
        // that bech32 address to TransactionModel.SenderWallet so the
        // /api/register/query/wallets/{address}/transactions lookup matches.
        var docket = CreateDocketWithTransaction(
            transactionId: "tx-bech32",
            signedBy: Bech32Address);

        var model = DocketSerializer.ToRegisterModel(docket);

        model.Transactions.Should().ContainSingle();
        model.Transactions[0].SenderWallet.Should().Be(Bech32Address);
    }

    [Fact]
    public void ToRegisterModel_SignatureWithoutSignedBy_FallsBackToBase64UrlPublicKey()
    {
        // Legacy fallback path: when SignedBy is missing (e.g. genesis
        // transactions with no Wallet Service round-trip), the persisted
        // SenderWallet should still be non-empty — derived from the raw
        // public key as base64url. It will not match a bech32 wallet
        // lookup, but it preserves a deterministic, queryable identity.
        var docket = CreateDocketWithTransaction(
            transactionId: "tx-no-signedby",
            signedBy: null);

        var model = DocketSerializer.ToRegisterModel(docket);

        model.Transactions.Should().ContainSingle();
        model.Transactions[0].SenderWallet.Should().NotBeNullOrEmpty();
        model.Transactions[0].SenderWallet.Should().NotStartWith("ws1",
            "the fallback path uses base64url(PublicKey), not bech32 encoding");
    }

    [Fact]
    public void ToRegisterModel_SignatureWithWhitespaceSignedBy_FallsBackToBase64UrlPublicKey()
    {
        // Whitespace-only SignedBy should be treated as missing and fall
        // through to the base64url fallback. Pins the IsNullOrWhiteSpace
        // behaviour so a refactor doesn't regress to a Length>0 check
        // (which would happily emit "   " as a wallet address).
        var docket = CreateDocketWithTransaction(
            transactionId: "tx-whitespace",
            signedBy: "   ");

        var model = DocketSerializer.ToRegisterModel(docket);

        model.Transactions[0].SenderWallet.Should().NotBe("   ");
        model.Transactions[0].SenderWallet.Should().NotStartWith("ws1");
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
            ProposerSignature = new Signature
            {
                PublicKey = new byte[] { 1, 2, 3 },
                SignatureValue = new byte[] { 4, 5, 6 },
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            },
            Transactions = new List<Transaction>
            {
                new()
                {
                    TransactionId = transactionId,
                    RegisterId = "register-1",
                    BlueprintId = "blueprint-1",
                    ActionId = "1",
                    Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
                    PayloadHash = $"hash-{transactionId}",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Priority = TransactionPriority.Normal,
                    Signatures = new List<Signature>
                    {
                        new()
                        {
                            PublicKey = new byte[] { 1, 2, 3 },
                            SignatureValue = new byte[] { 4, 5, 6 },
                            Algorithm = "ED25519",
                            SignedAt = DateTimeOffset.UtcNow,
                            SignedBy = signedBy
                        }
                    },
                    Metadata = new Dictionary<string, string>()
                }
            }
        };
    }
}
