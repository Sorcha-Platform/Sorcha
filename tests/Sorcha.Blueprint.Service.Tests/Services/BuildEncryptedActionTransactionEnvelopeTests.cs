// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Cryptography.Enums;
using Sorcha.TransactionHandler.Encryption.Models;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Covers what <see cref="TransactionBuilderServiceExtensions.BuildEncryptedActionTransactionAsync"/>
/// actually writes onto the sealed envelope — the wire-shape half of issues #1695 (plaintextHash)
/// and #1684 L1 (disclosedFields). A static extension method over plain POCOs, so it is exercised
/// directly rather than through a mocked <see cref="ITransactionBuilderService"/>.
/// </summary>
public class BuildEncryptedActionTransactionEnvelopeTests
{
    private static readonly BlueprintModel Blueprint = new()
    {
        Id = "bp-1",
        Title = "Test Blueprint"
    };

    private static readonly Instance TestInstance = new()
    {
        Id = "inst-1",
        BlueprintId = "bp-1",
        BlueprintVersion = 1,
        RegisterId = "reg-1",
        TenantId = "tenant-1"
    };

    private static readonly ActionModel Action = new()
    {
        Id = 1,
        Title = "Action 1"
    };

    private static EncryptedPayloadGroup[] SingleGroup() =>
    [
        new EncryptedPayloadGroup
        {
            GroupId = "group-1",
            DisclosedFields = ["/name", "/amount"],
            Ciphertext = [0xDE, 0xAD, 0xBE, 0xEF],
            Nonce = new byte[24],
            EncryptionAlgorithm = EncryptionType.XCHACHA20_POLY1305,
            WrappedKeys =
            [
                new WrappedKey
                {
                    WalletAddress = "wallet-recipient-1",
                    EncryptedKey = new byte[48],
                    Algorithm = WalletNetworks.ED25519
                }
            ]
        }
    ];

    [Fact]
    public async Task BuildEncryptedActionTransactionAsync_NewEnvelope_DoesNotPublishPlaintextHash()
    {
        // Act — the "service" parameter is unused by the extension method's body (an extension
        // method needs a receiver, but this one has nothing to call on it).
        var built = await Mock.Of<ITransactionBuilderService>().BuildEncryptedActionTransactionAsync(
            Blueprint, TestInstance, Action, payloadData: [], SingleGroup(), previousTransactionId: null);

        var json = System.Text.Encoding.UTF8.GetString(built.TransactionData);

        // Assert — #1695: no confirmation-oracle hash on the sealed envelope.
        json.Should().NotContain("plaintextHash");
    }

    [Fact]
    public async Task BuildEncryptedActionTransactionAsync_NewEnvelope_StillCarriesTheProtectedFields()
    {
        // Guards against a vacuous pass on the sibling test above — proves the JSON produced is the
        // real envelope shape (ciphertext, nonce, wrappedKeys), not an empty or malformed document.
        var built = await Mock.Of<ITransactionBuilderService>().BuildEncryptedActionTransactionAsync(
            Blueprint, TestInstance, Action, payloadData: [], SingleGroup(), previousTransactionId: null);

        using var doc = JsonDocument.Parse(built.TransactionData);
        var group = doc.RootElement.GetProperty("encryptedPayloads")[0];

        group.GetProperty("groupId").GetString().Should().Be("group-1");
        group.TryGetProperty("ciphertext", out _).Should().BeTrue();
        group.TryGetProperty("nonce", out _).Should().BeTrue();
        group.GetProperty("wrappedKeys")[0].GetProperty("walletAddress").GetString()
            .Should().Be("wallet-recipient-1");
    }
}
