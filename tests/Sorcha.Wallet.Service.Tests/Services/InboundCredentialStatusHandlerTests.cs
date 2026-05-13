// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Services.Implementation;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Multi-node audit CRITICAL #2 — coverage for <see cref="InboundCredentialStatusHandler"/>.
/// </summary>
public class InboundCredentialStatusHandlerTests
{
    private const string HolderWallet = "ws11qholder";
    private const string IssuerWallet = "ws11qissuer";
    private const string CredentialId = "urn:uuid:test-credential-1";
    private const string RegisterId = "abc123";
    private const string TxId = "deadbeef";

    [Fact]
    public async Task TryApply_StatusChangeForLocalCredential_AppliesUpdate()
    {
        var register = new Mock<IRegisterServiceClient>();
        var store = new Mock<ICredentialStore>();

        register.Setup(r => r.GetTransactionAsync(RegisterId, TxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildStatusChangeTx(NewStatus: "Revoked"));

        store.Setup(s => s.GetByIdForWalletAsync(CredentialId, HolderWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialEntity
            {
                Id = CredentialId,
                Type = "TestCredential",
                IssuerDid = IssuerWallet,
                SubjectDid = HolderWallet,
                WalletAddress = HolderWallet,
                ClaimsJson = "{}",
                RawToken = "fake-token",
                IssuedAt = DateTimeOffset.UtcNow,
                Status = CredentialStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        store.Setup(s => s.UpdateStatusAsync(CredentialId, HolderWallet, CredentialStatus.Revoked, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new InboundCredentialStatusHandler(
            register.Object, store.Object, NullLogger<InboundCredentialStatusHandler>.Instance);

        var result = await handler.TryApplyAsync(HolderWallet, TxId, RegisterId, CancellationToken.None);

        result.Applied.Should().BeTrue();
        result.CredentialId.Should().Be(CredentialId);
        result.NewStatus.Should().Be("Revoked");
        store.Verify(s => s.UpdateStatusAsync(CredentialId, HolderWallet, CredentialStatus.Revoked, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryApply_NonStatusChangeTransaction_Skips()
    {
        var register = new Mock<IRegisterServiceClient>();
        var store = new Mock<ICredentialStore>();

        register.Setup(r => r.GetTransactionAsync(RegisterId, TxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionModel
            {
                TxId = TxId,
                RegisterId = RegisterId,
                MetaData = new TransactionMetaData { RegisterId = RegisterId, TransactionType = TransactionType.Action },
            });

        var handler = new InboundCredentialStatusHandler(
            register.Object, store.Object, NullLogger<InboundCredentialStatusHandler>.Instance);

        var result = await handler.TryApplyAsync(HolderWallet, TxId, RegisterId, CancellationToken.None);

        result.Applied.Should().BeFalse();
        store.Verify(s => s.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CredentialStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryApply_PayloadIssuerMismatchesTxSender_Drops()
    {
        var register = new Mock<IRegisterServiceClient>();
        var store = new Mock<ICredentialStore>();

        var tx = BuildStatusChangeTx(NewStatus: "Revoked");
        tx.SenderWallet = "ws11qhostile";  // Tx sender doesn't match payload's claimed issuer.

        register.Setup(r => r.GetTransactionAsync(RegisterId, TxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);

        var handler = new InboundCredentialStatusHandler(
            register.Object, store.Object, NullLogger<InboundCredentialStatusHandler>.Instance);

        var result = await handler.TryApplyAsync(HolderWallet, TxId, RegisterId, CancellationToken.None);

        result.Applied.Should().BeFalse();
        store.Verify(s => s.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CredentialStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryApply_PayloadIssuerMismatchesLocalCredentialIssuer_Drops()
    {
        var register = new Mock<IRegisterServiceClient>();
        var store = new Mock<ICredentialStore>();

        register.Setup(r => r.GetTransactionAsync(RegisterId, TxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildStatusChangeTx(NewStatus: "Revoked"));

        // Local credential was issued by a DIFFERENT issuer than the one in the payload.
        store.Setup(s => s.GetByIdForWalletAsync(CredentialId, HolderWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialEntity
            {
                Id = CredentialId,
                Type = "TestCredential",
                IssuerDid = "ws11qsomeoneelse",
                SubjectDid = HolderWallet,
                WalletAddress = HolderWallet,
                ClaimsJson = "{}",
                RawToken = "fake-token",
                IssuedAt = DateTimeOffset.UtcNow,
                Status = CredentialStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        var handler = new InboundCredentialStatusHandler(
            register.Object, store.Object, NullLogger<InboundCredentialStatusHandler>.Instance);

        var result = await handler.TryApplyAsync(HolderWallet, TxId, RegisterId, CancellationToken.None);

        result.Applied.Should().BeFalse();
        store.Verify(s => s.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CredentialStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryApply_LocalCredentialMissing_Skips()
    {
        var register = new Mock<IRegisterServiceClient>();
        var store = new Mock<ICredentialStore>();

        register.Setup(r => r.GetTransactionAsync(RegisterId, TxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildStatusChangeTx(NewStatus: "Revoked"));

        store.Setup(s => s.GetByIdForWalletAsync(CredentialId, HolderWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialEntity?)null);

        var handler = new InboundCredentialStatusHandler(
            register.Object, store.Object, NullLogger<InboundCredentialStatusHandler>.Instance);

        var result = await handler.TryApplyAsync(HolderWallet, TxId, RegisterId, CancellationToken.None);

        result.Applied.Should().BeFalse();
    }

    [Fact]
    public async Task TryApply_UnsupportedStatus_Skips()
    {
        var register = new Mock<IRegisterServiceClient>();
        var store = new Mock<ICredentialStore>();

        register.Setup(r => r.GetTransactionAsync(RegisterId, TxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildStatusChangeTx(NewStatus: "Expired"));  // Holder-local-only state, not propagated by issuer.

        store.Setup(s => s.GetByIdForWalletAsync(CredentialId, HolderWallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialEntity
            {
                Id = CredentialId,
                Type = "TestCredential",
                IssuerDid = IssuerWallet,
                SubjectDid = HolderWallet,
                WalletAddress = HolderWallet,
                ClaimsJson = "{}",
                RawToken = "fake-token",
                IssuedAt = DateTimeOffset.UtcNow,
                Status = CredentialStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
            });

        var handler = new InboundCredentialStatusHandler(
            register.Object, store.Object, NullLogger<InboundCredentialStatusHandler>.Instance);

        var result = await handler.TryApplyAsync(HolderWallet, TxId, RegisterId, CancellationToken.None);

        result.Applied.Should().BeFalse();
        store.Verify(s => s.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CredentialStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryApply_RegisterClientThrows_DoesNotPropagate()
    {
        var register = new Mock<IRegisterServiceClient>();
        var store = new Mock<ICredentialStore>();

        register.Setup(r => r.GetTransactionAsync(RegisterId, TxId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("register down"));

        var handler = new InboundCredentialStatusHandler(
            register.Object, store.Object, NullLogger<InboundCredentialStatusHandler>.Instance);

        var result = await handler.TryApplyAsync(HolderWallet, TxId, RegisterId, CancellationToken.None);

        result.Applied.Should().BeFalse();
    }

    private static TransactionModel BuildStatusChangeTx(string NewStatus)
    {
        var payload = new CredentialStatusChangePayload
        {
            CredentialId = CredentialId,
            NewStatus = NewStatus,
            IssuerWallet = IssuerWallet,
            SubjectDid = HolderWallet,
            ChangedAt = DateTimeOffset.UtcNow,
        };
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        return new TransactionModel
        {
            TxId = TxId,
            RegisterId = RegisterId,
            SenderWallet = IssuerWallet,
            RecipientsWallets = new List<string> { HolderWallet },
            TimeStamp = DateTime.UtcNow,
            PrevTxId = string.Empty,
            MetaData = new TransactionMetaData
            {
                RegisterId = RegisterId,
                TransactionType = TransactionType.CredentialStatusChange,
            },
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    Data = Convert.ToBase64String(bytes),
                    ContentType = "application/json",
                    ContentEncoding = "base64",
                },
            },
            Signature = IssuerWallet,
        };
    }
}
