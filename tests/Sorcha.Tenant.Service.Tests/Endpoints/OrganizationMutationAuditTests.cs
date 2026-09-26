// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Moq;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Endpoints;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Org create/update/deactivate and org-user add/update/remove used to be audited only from the
/// UI, via a client-side POST to <c>/api/audit</c> — a route no service ever mapped. The failure
/// was swallowed, so every one of these six events was silently lost, and a client-authored audit
/// trail can be skipped or forged regardless (#1655). The audit entry now belongs to the Tenant
/// Service endpoint that performs the mutation, written only on the success path.
/// </summary>
public class OrganizationMutationAuditTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Guid _organizationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _actorId = Guid.NewGuid();

    public OrganizationMutationAuditTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task CreateOrganization_Success_WritesOrganizationCreatedAudit()
    {
        var response = new OrganizationResponse
        {
            Id = _organizationId,
            Name = "Acme Corporation",
            Subdomain = "acme-corp",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var service = new Mock<IOrganizationService>();
        service
            .Setup(s => s.CreateOrganizationAsync(It.IsAny<CreateOrganizationRequest>(), _actorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var request = new CreateOrganizationRequest { Name = "Acme Corporation", Subdomain = "acme-corp" };

        await InvokeCreateOrganizationAsync(request, service.Object);

        _dbContext.AuditLogEntries.Should().ContainSingle(e =>
            e.EventType == AuditEventType.OrganizationCreated
            && e.Success
            && e.OrganizationId == _organizationId
            && e.IdentityId == _actorId);
    }

    [Fact]
    public async Task UpdateOrganization_Success_WritesOrganizationUpdatedAudit()
    {
        var response = new OrganizationResponse
        {
            Id = _organizationId,
            Name = "Renamed",
            Subdomain = "acme-corp",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var service = new Mock<IOrganizationService>();
        service
            .Setup(s => s.UpdateOrganizationAsync(_organizationId, It.IsAny<UpdateOrganizationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        await InvokeUpdateOrganizationAsync(_organizationId, new UpdateOrganizationRequest { Name = "Renamed" }, service.Object);

        _dbContext.AuditLogEntries.Should().ContainSingle(e =>
            e.EventType == AuditEventType.OrganizationUpdated
            && e.Success
            && e.OrganizationId == _organizationId
            && e.IdentityId == _actorId);
    }

    [Fact]
    public async Task UpdateOrganization_NotFound_WritesNoAudit()
    {
        var service = new Mock<IOrganizationService>();
        service
            .Setup(s => s.UpdateOrganizationAsync(_organizationId, It.IsAny<UpdateOrganizationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationResponse?)null);

        await InvokeUpdateOrganizationAsync(_organizationId, new UpdateOrganizationRequest { Name = "Renamed" }, service.Object);

        _dbContext.AuditLogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task DeactivateOrganization_Success_WritesOrganizationDeactivatedAudit()
    {
        var service = new Mock<IOrganizationService>();
        service
            .Setup(s => s.DeactivateOrganizationAsync(_organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await InvokeDeactivateOrganizationAsync(_organizationId, service.Object);

        _dbContext.AuditLogEntries.Should().ContainSingle(e =>
            e.EventType == AuditEventType.OrganizationDeactivated
            && e.Success
            && e.OrganizationId == _organizationId
            && e.IdentityId == _actorId);
    }

    [Fact]
    public async Task DeactivateOrganization_NotFound_WritesNoAudit()
    {
        var service = new Mock<IOrganizationService>();
        service
            .Setup(s => s.DeactivateOrganizationAsync(_organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await InvokeDeactivateOrganizationAsync(_organizationId, service.Object);

        _dbContext.AuditLogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task AddUserToOrganization_Success_WritesUserAddedAudit()
    {
        var targetUserId = Guid.NewGuid();
        var response = new UserResponse
        {
            Id = targetUserId,
            OrganizationId = _organizationId,
            Email = "new.user@example.com",
            DisplayName = "New User",
            Roles = [UserRole.Consumer],
            Status = IdentityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var service = new Mock<IOrganizationService>();
        service
            .Setup(s => s.AddUserToOrganizationAsync(_organizationId, It.IsAny<AddUserToOrganizationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var request = new AddUserToOrganizationRequest
        {
            Email = "new.user@example.com",
            DisplayName = "New User",
            ExternalIdpSubject = "sub-123"
        };

        await InvokeAddUserToOrganizationAsync(_organizationId, request, service.Object);

        _dbContext.AuditLogEntries.Should().ContainSingle(e =>
            e.EventType == AuditEventType.UserAddedToOrganization
            && e.Success
            && e.OrganizationId == _organizationId
            && e.IdentityId == _actorId
            && e.Details != null
            && e.Details["targetUserId"].ToString() == targetUserId.ToString());
    }

    [Fact]
    public async Task UpdateOrganizationUser_Success_WritesUserUpdatedAudit()
    {
        var targetUserId = Guid.NewGuid();
        var response = new UserResponse
        {
            Id = targetUserId,
            OrganizationId = _organizationId,
            Email = "user@example.com",
            DisplayName = "Updated Name",
            Roles = [UserRole.Consumer],
            Status = IdentityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var service = new Mock<IOrganizationService>();
        service
            .Setup(s => s.UpdateOrganizationUserAsync(_organizationId, targetUserId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        await InvokeUpdateOrganizationUserAsync(
            _organizationId, targetUserId, new UpdateUserRequest { DisplayName = "Updated Name" }, service.Object);

        _dbContext.AuditLogEntries.Should().ContainSingle(e =>
            e.EventType == AuditEventType.UserUpdatedInOrganization
            && e.Success
            && e.OrganizationId == _organizationId
            && e.IdentityId == _actorId
            && e.Details != null
            && e.Details["targetUserId"].ToString() == targetUserId.ToString());
    }

    [Fact]
    public async Task UpdateOrganizationUser_NotFound_WritesNoAudit()
    {
        var targetUserId = Guid.NewGuid();
        var service = new Mock<IOrganizationService>();
        service
            .Setup(s => s.UpdateOrganizationUserAsync(_organizationId, targetUserId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserResponse?)null);

        await InvokeUpdateOrganizationUserAsync(
            _organizationId, targetUserId, new UpdateUserRequest { DisplayName = "Updated Name" }, service.Object);

        _dbContext.AuditLogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveUserFromOrganization_Success_WritesUserRemovedAudit()
    {
        var targetUserId = Guid.NewGuid();
        var service = new Mock<IOrganizationService>();
        service
            .Setup(s => s.RemoveUserFromOrganizationAsync(_organizationId, targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await InvokeRemoveUserFromOrganizationAsync(_organizationId, targetUserId, service.Object);

        _dbContext.AuditLogEntries.Should().ContainSingle(e =>
            e.EventType == AuditEventType.UserRemovedFromOrganization
            && e.Success
            && e.OrganizationId == _organizationId
            && e.IdentityId == _actorId
            && e.Details != null
            && e.Details["targetUserId"].ToString() == targetUserId.ToString());
    }

    [Fact]
    public async Task RemoveUserFromOrganization_NotFound_WritesNoAudit()
    {
        var targetUserId = Guid.NewGuid();
        var service = new Mock<IOrganizationService>();
        service
            .Setup(s => s.RemoveUserFromOrganizationAsync(_organizationId, targetUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await InvokeRemoveUserFromOrganizationAsync(_organizationId, targetUserId, service.Object);

        _dbContext.AuditLogEntries.Should().BeEmpty();
    }

    private ClaimsPrincipal ActorPrincipal() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, _actorId.ToString())], "test"));

    private async Task<object?> InvokeCreateOrganizationAsync(
        CreateOrganizationRequest request, IOrganizationService service)
    {
        var method = typeof(OrganizationEndpoints).GetMethod("CreateOrganization", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(null, [request, service, _dbContext, ActorPrincipal(), CancellationToken.None])!;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private async Task<object?> InvokeUpdateOrganizationAsync(
        Guid id, UpdateOrganizationRequest request, IOrganizationService service)
    {
        var method = typeof(OrganizationEndpoints).GetMethod("UpdateOrganization", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(null, [id, request, service, _dbContext, ActorPrincipal(), CancellationToken.None])!;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private async Task<object?> InvokeDeactivateOrganizationAsync(Guid id, IOrganizationService service)
    {
        var method = typeof(OrganizationEndpoints).GetMethod("DeactivateOrganization", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(null, [id, service, _dbContext, ActorPrincipal(), CancellationToken.None])!;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private async Task<object?> InvokeAddUserToOrganizationAsync(
        Guid organizationId, AddUserToOrganizationRequest request, IOrganizationService service)
    {
        var method = typeof(OrganizationEndpoints).GetMethod("AddUserToOrganization", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(null, [organizationId, request, service, _dbContext, ActorPrincipal(), CancellationToken.None])!;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private async Task<object?> InvokeUpdateOrganizationUserAsync(
        Guid organizationId, Guid userId, UpdateUserRequest request, IOrganizationService service)
    {
        var method = typeof(OrganizationEndpoints).GetMethod("UpdateOrganizationUser", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(null, [organizationId, userId, request, service, _dbContext, ActorPrincipal(), CancellationToken.None])!;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private async Task<object?> InvokeRemoveUserFromOrganizationAsync(
        Guid organizationId, Guid userId, IOrganizationService service)
    {
        var method = typeof(OrganizationEndpoints).GetMethod("RemoveUserFromOrganization", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var task = (Task)method!.Invoke(null, [organizationId, userId, service, _dbContext, ActorPrincipal(), CancellationToken.None])!;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }
}
