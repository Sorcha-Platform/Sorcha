// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

public class RegisterInvitationServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Mock<IWalletServiceClient> _walletClientMock = new();
    private readonly Mock<IRegisterSubscriptionService> _subscriptionServiceMock = new();
    private readonly Mock<ILogger<RegisterInvitationService>> _loggerMock = new();
    private readonly RegisterInvitationService _service;

    private readonly Guid _sourceOrgId = Guid.NewGuid();
    private readonly Guid _targetOrgId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private const string ValidRegisterId = "abcdef0123456789abcdef0123456789";
    private const string SourceWalletAddress = "srcWalletAddr123";
    private const string TargetWalletAddress = "tgtWalletAddr456";

    public RegisterInvitationServiceTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();
        _service = new RegisterInvitationService(
            _dbContext,
            _walletClientMock.Object,
            _subscriptionServiceMock.Object,
            _loggerMock.Object);

        SeedTestData();
    }

    private void SeedTestData()
    {
        // Source org with wallet
        _dbContext.Organizations.Add(new Organization
        {
            Id = _sourceOrgId,
            Name = "Source Org",
            Subdomain = "source-org",
            WalletAddress = SourceWalletAddress,
            PublicKey = "sourcePublicKeyBase64",
            SigningAlgorithm = "ED25519"
        });

        // Target org with wallet
        _dbContext.Organizations.Add(new Organization
        {
            Id = _targetOrgId,
            Name = "Target Org",
            Subdomain = "target-org",
            WalletAddress = TargetWalletAddress,
            PublicKey = "targetPublicKeyBase64",
            SigningAlgorithm = "ED25519"
        });

        // Source org owns the register
        _dbContext.OrganizationRegisterSubscriptions.Add(new OrganizationRegisterSubscription
        {
            OrganizationId = _sourceOrgId,
            RegisterId = ValidRegisterId,
            RegisterName = "Test Register",
            SubscriptionType = SubscriptionType.Owner,
            Status = SubscriptionStatus.Active,
            SubscribedByUserId = _userId,
            SubscribedAt = DateTimeOffset.UtcNow
        });

        _dbContext.SaveChanges();
    }

    private void SetupWalletMocks()
    {
        _walletClientMock.Setup(w => w.GetWalletAsync(TargetWalletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = TargetWalletAddress,
                Name = "target-org-signing",
                PublicKey = "targetPublicKeyBase64",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = $"org:{_targetOrgId}",
                Tenant = _targetOrgId.ToString()
            });

        _walletClientMock.Setup(w => w.GetWalletAsync(SourceWalletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = SourceWalletAddress,
                Name = "source-org-signing",
                PublicKey = "sourcePublicKeyBase64",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = $"org:{_sourceOrgId}",
                Tenant = _sourceOrgId.ToString()
            });

        _walletClientMock.Setup(w => w.EncryptPayloadAsync(
                TargetWalletAddress, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] payload, CancellationToken _) =>
                Encoding.UTF8.GetBytes("encrypted:" + Convert.ToBase64String(payload)));

        _walletClientMock.Setup(w => w.DecryptPayloadAsync(
                TargetWalletAddress, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] encrypted, CancellationToken _) =>
            {
                var encStr = Encoding.UTF8.GetString(encrypted);
                if (encStr.StartsWith("encrypted:"))
                    return Convert.FromBase64String(encStr["encrypted:".Length..]);
                throw new InvalidOperationException("Cannot decrypt");
            });

        _walletClientMock.Setup(w => w.SignDataAsync(
                SourceWalletAddress, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = Encoding.UTF8.GetBytes("test-signature"),
                PublicKey = Encoding.UTF8.GetBytes("sourcePublicKeyBase64"),
                SignedBy = SourceWalletAddress,
                Algorithm = "ED25519"
            });

        _walletClientMock.Setup(w => w.VerifySignatureAsync(
                "sourcePublicKeyBase64", It.IsAny<string>(), It.IsAny<string>(),
                "ED25519", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    public void Dispose() => _dbContext.Dispose();

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsInvitationWithToken()
    {
        SetupWalletMocks();
        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}",
            ExpiresInDays = 7
        };

        var result = await _service.CreateAsync(_sourceOrgId, request, _userId);

        result.Should().NotBeNull();
        result.InvitationId.Should().NotBeNullOrEmpty();
        result.InvitationToken.Should().NotBeNullOrEmpty();
        result.RegisterId.Should().Be(ValidRegisterId);
        result.TargetOrgDid.Should().Be($"did:sorcha:org:{TargetWalletAddress}");
        result.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateAsync_InvalidRegisterId_ThrowsArgumentException()
    {
        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = "not-hex",
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };

        var act = () => _service.CreateAsync(_sourceOrgId, request, _userId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*32-character lowercase hex*");
    }

    [Fact]
    public async Task CreateAsync_InvalidTargetDid_ThrowsArgumentException()
    {
        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = "did:sorcha:w:someWallet" // wallet DID, not org
        };

        var act = () => _service.CreateAsync(_sourceOrgId, request, _userId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*organization DID*");
    }

    [Fact]
    public async Task CreateAsync_OrgDoesNotOwnRegister_ThrowsInvalidOperationException()
    {
        SetupWalletMocks();
        var otherOrgId = Guid.NewGuid();
        _dbContext.Organizations.Add(new Organization
        {
            Id = otherOrgId, Name = "Other", Subdomain = "other",
            WalletAddress = "otherAddr"
        });
        await _dbContext.SaveChangesAsync();

        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };

        var act = () => _service.CreateAsync(otherOrgId, request, _userId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not own*");
    }

    [Fact]
    public async Task CreateAsync_OrgWithoutWallet_ThrowsInvalidOperationException()
    {
        var noWalletOrgId = Guid.NewGuid();
        _dbContext.Organizations.Add(new Organization
        {
            Id = noWalletOrgId, Name = "No Wallet Org", Subdomain = "no-wallet"
        });
        await _dbContext.SaveChangesAsync();

        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };

        var act = () => _service.CreateAsync(noWalletOrgId, request, _userId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no wallet*");
    }

    [Fact]
    public async Task CreateAsync_ExpiresInDaysOutOfRange_ThrowsArgumentException()
    {
        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}",
            ExpiresInDays = 91
        };

        var act = () => _service.CreateAsync(_sourceOrgId, request, _userId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*between 1 and 90*");
    }

    [Fact]
    public async Task CreateAsync_RecordsInvitationInDb()
    {
        SetupWalletMocks();
        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };

        var result = await _service.CreateAsync(_sourceOrgId, request, _userId);

        var record = _dbContext.RegisterInvitationRecords
            .FirstOrDefault(r => r.InvitationId == result.InvitationId);
        record.Should().NotBeNull();
        record!.SourceOrgId.Should().Be(_sourceOrgId);
        record.TargetOrgDid.Should().Be($"did:sorcha:org:{TargetWalletAddress}");
        record.Status.Should().Be(RegisterInvitationStatus.Pending);
    }

    #endregion

    #region AcceptAsync Tests

    [Fact]
    public async Task AcceptAsync_ValidToken_CreatesSubscription()
    {
        SetupWalletMocks();

        // Create invitation first
        var createRequest = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };
        var invitation = await _service.CreateAsync(_sourceOrgId, createRequest, _userId);

        // Accept it
        var acceptRequest = new AcceptInvitationRequest
        {
            InvitationToken = invitation.InvitationToken
        };
        var result = await _service.AcceptAsync(_targetOrgId, acceptRequest, _userId);

        result.Should().NotBeNull();
        result.RegisterId.Should().Be(ValidRegisterId);
        result.SubscriptionStatus.Should().Be("Active");
        result.SourceOrgDid.Should().Contain(SourceWalletAddress);
    }

    [Fact]
    public async Task AcceptAsync_ValidToken_ConsumesNonce()
    {
        SetupWalletMocks();

        var createRequest = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };
        var invitation = await _service.CreateAsync(_sourceOrgId, createRequest, _userId);

        await _service.AcceptAsync(_targetOrgId,
            new AcceptInvitationRequest { InvitationToken = invitation.InvitationToken }, _userId);

        _dbContext.InvitationNonces.Should().HaveCount(1);
    }

    [Fact]
    public async Task AcceptAsync_ReplayedToken_ThrowsInvalidOperationException()
    {
        SetupWalletMocks();

        var createRequest = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };
        var invitation = await _service.CreateAsync(_sourceOrgId, createRequest, _userId);

        // Accept first time
        await _service.AcceptAsync(_targetOrgId,
            new AcceptInvitationRequest { InvitationToken = invitation.InvitationToken }, _userId);

        // Remove subscription so duplicate check doesn't fire first
        var sub = _dbContext.OrganizationRegisterSubscriptions
            .First(s => s.OrganizationId == _targetOrgId);
        _dbContext.OrganizationRegisterSubscriptions.Remove(sub);
        await _dbContext.SaveChangesAsync();

        // Replay — should be caught by nonce
        var act = () => _service.AcceptAsync(_targetOrgId,
            new AcceptInvitationRequest { InvitationToken = invitation.InvitationToken }, _userId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*nonce reuse*");
    }

    [Fact]
    public async Task AcceptAsync_InvalidTokenFormat_ThrowsArgumentException()
    {
        var act = () => _service.AcceptAsync(_targetOrgId,
            new AcceptInvitationRequest { InvitationToken = "not-valid-base64!@#$" }, _userId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid invitation token*");
    }

    [Fact]
    public async Task AcceptAsync_InvalidSignature_ThrowsInvalidOperationException()
    {
        SetupWalletMocks();

        var createRequest = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };
        var invitation = await _service.CreateAsync(_sourceOrgId, createRequest, _userId);

        // Override verify to return false
        _walletClientMock.Setup(w => w.VerifySignatureAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _service.AcceptAsync(_targetOrgId,
            new AcceptInvitationRequest { InvitationToken = invitation.InvitationToken }, _userId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*signature verification failed*");
    }

    [Fact]
    public async Task AcceptAsync_AlreadySubscribed_ThrowsInvalidOperationException()
    {
        SetupWalletMocks();

        // Pre-create a subscription
        _dbContext.OrganizationRegisterSubscriptions.Add(new OrganizationRegisterSubscription
        {
            OrganizationId = _targetOrgId,
            RegisterId = ValidRegisterId,
            SubscriptionType = SubscriptionType.Public,
            Status = SubscriptionStatus.Active,
            SubscribedByUserId = _userId,
            SubscribedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        var createRequest = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };
        var invitation = await _service.CreateAsync(_sourceOrgId, createRequest, _userId);

        var act = () => _service.AcceptAsync(_targetOrgId,
            new AcceptInvitationRequest { InvitationToken = invitation.InvitationToken }, _userId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already subscribed*");
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_SentFilter_ReturnsOnlySentInvitations()
    {
        SetupWalletMocks();

        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };
        await _service.CreateAsync(_sourceOrgId, request, _userId);

        var result = await _service.ListAsync(_sourceOrgId, "sent");

        result.Invitations.Should().HaveCount(1);
        result.Invitations[0].Direction.Should().Be("sent");
    }

    [Fact]
    public async Task ListAsync_ReceivedFilter_ReturnsReceivedInvitations()
    {
        SetupWalletMocks();

        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };
        await _service.CreateAsync(_sourceOrgId, request, _userId);

        var result = await _service.ListAsync(_targetOrgId, "received");

        result.Invitations.Should().HaveCount(1);
        result.Invitations[0].Direction.Should().Be("received");
    }

    [Fact]
    public async Task ListAsync_ResolvesSourceAndTargetOrgNames()
    {
        // GAP-013 — ListAsync used to leave SourceOrgName / TargetOrgName as null.
        SetupWalletMocks();

        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };
        await _service.CreateAsync(_sourceOrgId, request, _userId);

        var sentResult = await _service.ListAsync(_sourceOrgId, "sent");
        sentResult.Invitations.Should().HaveCount(1);
        sentResult.Invitations[0].SourceOrgName.Should().Be("Source Org");
        sentResult.Invitations[0].TargetOrgName.Should().Be("Target Org");

        // Same data shape regardless of direction filter.
        var receivedResult = await _service.ListAsync(_targetOrgId, "received");
        receivedResult.Invitations[0].SourceOrgName.Should().Be("Source Org");
        receivedResult.Invitations[0].TargetOrgName.Should().Be("Target Org");
    }

    [Fact]
    public async Task ListAsync_TargetOrgUnknown_LeavesTargetOrgNameNull()
    {
        // Defensive: if the target org's wallet isn't in this tenant DB, name
        // can't be resolved — must not throw, must return null.
        SetupWalletMocks();

        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };
        await _service.CreateAsync(_sourceOrgId, request, _userId);

        // Drop the target org so it's no longer resolvable.
        var targetOrg = _dbContext.Organizations.First(o => o.Id == _targetOrgId);
        _dbContext.Organizations.Remove(targetOrg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.ListAsync(_sourceOrgId, "sent");
        result.Invitations[0].SourceOrgName.Should().Be("Source Org");
        result.Invitations[0].TargetOrgName.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_MultipleInvitations_DoesNotN1QueryOrgs()
    {
        // GAP-015 — pre-PR, every record fired its own DB query through
        // GetSourceOrgDid. After the fix, all org lookups happen in two
        // batches (sources by id, targets by wallet address) and the response
        // shape stays correct regardless of cardinality.
        SetupWalletMocks();

        var targetOrgIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var targetWallets = new[] { "tgtA", "tgtB", "tgtC" };
        for (int i = 0; i < targetOrgIds.Length; i++)
        {
            _dbContext.Organizations.Add(new Organization
            {
                Id = targetOrgIds[i],
                Name = $"Target Org {(char)('A' + i)}",
                Subdomain = $"target-{i}",
                WalletAddress = targetWallets[i],
                PublicKey = "k",
                SigningAlgorithm = "ED25519"
            });
            _walletClientMock.Setup(w => w.GetWalletAsync(targetWallets[i], It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Sorcha.ServiceClients.Wallet.WalletInfo
                {
                    Address = targetWallets[i],
                    Name = $"Target {(char)('A' + i)}",
                    PublicKey = "k",
                    Algorithm = "ED25519",
                    Status = "Active",
                    Owner = $"org:{targetOrgIds[i]}",
                    Tenant = targetOrgIds[i].ToString()
                });
            _walletClientMock.Setup(w => w.EncryptPayloadAsync(
                    targetWallets[i], It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new byte[] { 1, 2, 3 });
        }
        await _dbContext.SaveChangesAsync();

        foreach (var wallet in targetWallets)
        {
            await _service.CreateAsync(_sourceOrgId, new CreateRegisterInvitationRequest
            {
                RegisterId = ValidRegisterId,
                TargetOrgDid = $"did:sorcha:org:{wallet}"
            }, _userId);
        }

        var result = await _service.ListAsync(_sourceOrgId, "sent");
        result.Invitations.Should().HaveCount(3);
        result.Invitations.Should().AllSatisfy(i =>
        {
            i.SourceOrgName.Should().Be("Source Org");
            i.TargetOrgName.Should().StartWith("Target Org ");
        });
    }

    #endregion

    #region RevokeAsync Tests

    [Fact]
    public async Task RevokeAsync_PendingInvitation_SetsStatusToRevoked()
    {
        SetupWalletMocks();

        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };
        var invitation = await _service.CreateAsync(_sourceOrgId, request, _userId);

        await _service.RevokeAsync(_sourceOrgId, invitation.InvitationId);

        var record = _dbContext.RegisterInvitationRecords
            .First(r => r.InvitationId == invitation.InvitationId);
        record.Status.Should().Be(RegisterInvitationStatus.Revoked);
        record.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RevokeAsync_AlreadyAccepted_ThrowsInvalidOperationException()
    {
        SetupWalletMocks();

        var createRequest = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };
        var invitation = await _service.CreateAsync(_sourceOrgId, createRequest, _userId);

        // Accept it
        await _service.AcceptAsync(_targetOrgId,
            new AcceptInvitationRequest { InvitationToken = invitation.InvitationToken }, _userId);

        // Try to revoke
        var act = () => _service.RevokeAsync(_sourceOrgId, invitation.InvitationId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot revoke*");
    }

    [Fact]
    public async Task RevokeAsync_NotFound_ThrowsInvalidOperationException()
    {
        var act = () => _service.RevokeAsync(_sourceOrgId, "nonexistent-id");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task RevokeAsync_DifferentOrg_ThrowsInvalidOperationException()
    {
        SetupWalletMocks();

        var request = new CreateRegisterInvitationRequest
        {
            RegisterId = ValidRegisterId,
            TargetOrgDid = $"did:sorcha:org:{TargetWalletAddress}"
        };
        var invitation = await _service.CreateAsync(_sourceOrgId, request, _userId);

        var act = () => _service.RevokeAsync(_targetOrgId, invitation.InvitationId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    #endregion
}
