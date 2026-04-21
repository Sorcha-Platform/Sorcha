// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Register.Core.LocalRelationship;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Models.LocalRelationship;
using Xunit;

namespace Sorcha.Register.Core.Tests.LocalRelationship;

public class RegisterLocalRelationshipServiceTests
{
    private const string RegisterId = "7c4ebed1dc2b444f87782e58b424e8d3";
    private const string OwnerWalletAddress = "ws11qqowner000000000000000000000000000000000000000000000000000000";
    private const string LocalWalletAddress = "ws11qqlocal000000000000000000000000000000000000000000000000000000";

    private static readonly byte[] LocalValidatorKey = new byte[]
        { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };
    private static readonly byte[] OtherValidatorKey = new byte[]
        { 99, 98, 97, 96, 95, 94, 93, 92, 91, 90, 89, 88, 87, 86, 85, 84, 83, 82, 81, 80, 79, 78, 77, 76, 75, 74, 73, 72, 71, 70, 69, 68 };

    [Fact]
    public async Task Derive_OwnerAttestationMatchesLocalWallet_SetsIsOwner()
    {
        var record = BuildControlRecord(
            ownerAddress: LocalWalletAddress,
            validatorKeys: Array.Empty<byte[]>());

        var svc = BuildService(record, walletAddresses: new[] { LocalWalletAddress });
        var rel = await svc.DeriveAsync(RegisterId);

        rel.Should().NotBeNull();
        rel!.IsOwner.Should().BeTrue();
        rel.IsValidator.Should().BeFalse();
        rel.IsSubscriber.Should().BeFalse();
    }

    [Fact]
    public async Task Derive_ValidatorKeyOnRoster_SetsIsValidator()
    {
        var record = BuildControlRecord(
            ownerAddress: OwnerWalletAddress,
            validatorKeys: new[] { LocalValidatorKey });

        var svc = BuildService(record, walletAddresses: Array.Empty<string>(), validatorKey: LocalValidatorKey);
        var rel = await svc.DeriveAsync(RegisterId);

        rel.Should().NotBeNull();
        rel!.IsValidator.Should().BeTrue();
        rel.IsOwner.Should().BeFalse();
        rel.IsSubscriber.Should().BeFalse();
    }

    [Fact]
    public async Task Derive_OwnerAndValidator_BothFlagsSet()
    {
        var record = BuildControlRecord(
            ownerAddress: LocalWalletAddress,
            validatorKeys: new[] { LocalValidatorKey });

        var svc = BuildService(record, walletAddresses: new[] { LocalWalletAddress }, validatorKey: LocalValidatorKey);
        var rel = await svc.DeriveAsync(RegisterId);

        rel.Should().NotBeNull();
        rel!.IsOwner.Should().BeTrue();
        rel.IsValidator.Should().BeTrue();
    }

    [Fact]
    public async Task Derive_PlainSubscriber_ReturnsNoneRolesAndIsSubscriberTrue()
    {
        var record = BuildControlRecord(
            ownerAddress: OwnerWalletAddress,
            validatorKeys: new[] { OtherValidatorKey });

        var svc = BuildService(record, walletAddresses: new[] { LocalWalletAddress }, validatorKey: LocalValidatorKey);
        var rel = await svc.DeriveAsync(RegisterId);

        rel.Should().NotBeNull();
        rel!.Roles.Should().Be(RegisterRoleSet.None);
        rel.IsSubscriber.Should().BeTrue();
        rel.IsValidator.Should().BeFalse();
        rel.IsOwner.Should().BeFalse();
    }

    [Fact]
    public async Task Derive_NoGenesisDocket_ReturnsNull()
    {
        var repo = new Mock<IReadOnlyRegisterRepository>();
        repo.Setup(r => r.GetDocketAsync(RegisterId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Docket?)null);

        var svc = new RegisterLocalRelationshipService(
            repo.Object,
            new StaticIdentityProvider(new LocalIdentitySnapshot(Array.Empty<string>(), null)),
            NullLogger<RegisterLocalRelationshipService>.Instance);

        var rel = await svc.DeriveAsync(RegisterId);
        rel.Should().BeNull();
    }

    [Fact]
    public async Task Derive_NoGenesisDocketButRegisterRowStashesControlRecord_DerivesFromStash()
    {
        // Bootstrap fix: before the genesis docket seals we can still enrol the validator
        // by deriving roles from the stashed InitialControlRecord on the Register row.
        var record = BuildControlRecord(
            ownerAddress: LocalWalletAddress,
            validatorKeys: new[] { LocalValidatorKey });

        var repo = new Mock<IReadOnlyRegisterRepository>();
        repo.Setup(r => r.GetDocketAsync(RegisterId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Docket?)null);
        repo.Setup(r => r.GetRegisterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = RegisterId,
                Name = "Test Register",
                InitialControlRecord = record
            });

        var svc = new RegisterLocalRelationshipService(
            repo.Object,
            new StaticIdentityProvider(new LocalIdentitySnapshot(new[] { LocalWalletAddress }, LocalValidatorKey)),
            NullLogger<RegisterLocalRelationshipService>.Instance);

        var rel = await svc.DeriveAsync(RegisterId);

        rel.Should().NotBeNull();
        rel!.IsOwner.Should().BeTrue();
        rel.IsValidator.Should().BeTrue();
        rel.ControlRecordVersion.Should().Be(0);
    }

    [Fact]
    public async Task Derive_CachesPerRegister_SecondCallDoesNotReReadRepository()
    {
        var record = BuildControlRecord(
            ownerAddress: LocalWalletAddress,
            validatorKeys: Array.Empty<byte[]>());

        var repo = BuildRepoMock(record);
        var svc = new RegisterLocalRelationshipService(
            repo.Object,
            new StaticIdentityProvider(new LocalIdentitySnapshot(new[] { LocalWalletAddress }, null)),
            NullLogger<RegisterLocalRelationshipService>.Instance);

        _ = await svc.DeriveAsync(RegisterId);
        _ = await svc.DeriveAsync(RegisterId);

        repo.Verify(r => r.GetDocketAsync(RegisterId, 0, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetTransactionsByDocketAsync(RegisterId, 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Invalidate_ForcesReRead()
    {
        var record = BuildControlRecord(
            ownerAddress: LocalWalletAddress,
            validatorKeys: Array.Empty<byte[]>());

        var repo = BuildRepoMock(record);
        var svc = new RegisterLocalRelationshipService(
            repo.Object,
            new StaticIdentityProvider(new LocalIdentitySnapshot(new[] { LocalWalletAddress }, null)),
            NullLogger<RegisterLocalRelationshipService>.Instance);

        _ = await svc.DeriveAsync(RegisterId);
        svc.Invalidate(RegisterId);
        _ = await svc.DeriveAsync(RegisterId);

        repo.Verify(r => r.GetDocketAsync(RegisterId, 0, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Derive_LegacyRegisterWithoutControlTx_ReturnsNoneRoles()
    {
        // Genesis exists but has no Control tx (pre-086 heuristic).
        var repo = new Mock<IReadOnlyRegisterRepository>();
        repo.Setup(r => r.GetDocketAsync(RegisterId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Docket { Id = 0, RegisterId = RegisterId });
        repo.Setup(r => r.GetTransactionsByDocketAsync(RegisterId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TransactionModel>());

        var svc = new RegisterLocalRelationshipService(
            repo.Object,
            new StaticIdentityProvider(new LocalIdentitySnapshot(new[] { LocalWalletAddress }, null)),
            NullLogger<RegisterLocalRelationshipService>.Instance);

        var rel = await svc.DeriveAsync(RegisterId);
        rel.Should().NotBeNull();
        rel!.Roles.Should().Be(RegisterRoleSet.None);
        rel.IsSubscriber.Should().BeTrue();
    }

    // -------- test helpers --------

    private static RegisterLocalRelationshipService BuildService(
        RegisterControlRecord controlRecord,
        IReadOnlyCollection<string> walletAddresses,
        byte[]? validatorKey = null)
    {
        var repo = BuildRepoMock(controlRecord);
        return new RegisterLocalRelationshipService(
            repo.Object,
            new StaticIdentityProvider(new LocalIdentitySnapshot(walletAddresses, validatorKey)),
            NullLogger<RegisterLocalRelationshipService>.Instance);
    }

    private static Mock<IReadOnlyRegisterRepository> BuildRepoMock(RegisterControlRecord controlRecord)
    {
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(controlRecord);
        var controlTx = new TransactionModel
        {
            RegisterId = RegisterId,
            TxId = new string('0', 64),
            SenderWallet = "system",
            MetaData = new TransactionMetaData { TransactionType = TransactionType.Control },
            Payloads = new[]
            {
                new PayloadModel
                {
                    Data = Convert.ToBase64String(payloadJson),
                    Hash = string.Empty
                }
            },
            Signature = string.Empty
        };

        var repo = new Mock<IReadOnlyRegisterRepository>();
        repo.Setup(r => r.GetDocketAsync(RegisterId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Docket { Id = 0, RegisterId = RegisterId });
        repo.Setup(r => r.GetTransactionsByDocketAsync(RegisterId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { controlTx });
        return repo;
    }

    private static RegisterControlRecord BuildControlRecord(
        string ownerAddress,
        IReadOnlyCollection<byte[]> validatorKeys)
    {
        var record = new RegisterControlRecord
        {
            RegisterId = RegisterId,
            Name = "Test Register",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = new List<RegisterAttestation>
            {
                new()
                {
                    Role = RegisterRole.Owner,
                    Subject = $"did:sorcha:wallet:{ownerAddress}",
                    PublicKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("pk")),
                    Signature = Convert.ToBase64String(Encoding.UTF8.GetBytes("sig")),
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow
                }
            }
        };

        if (validatorKeys.Count > 0)
        {
            record.Validators = new ValidatorRoster
            {
                Version = 1,
                RequiredSignatures = 1,
                Validators = validatorKeys.Select((k, i) => new ValidatorRosterEntry
                {
                    ValidatorId = $"validator-{i}",
                    PublicKey = Convert.ToBase64String(k),
                    Algorithm = SignatureAlgorithm.ED25519,
                    Status = ValidatorKeyStatus.Active
                }).ToList()
            };
        }

        return record;
    }

    private sealed class StaticIdentityProvider(LocalIdentitySnapshot snapshot) : ILocalIdentityProvider
    {
        public ValueTask<LocalIdentitySnapshot> GetAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(snapshot);
        public void Invalidate() { }
    }
}
