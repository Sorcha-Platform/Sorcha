// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.ServiceClients.Audit;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// #1648 — <c>POST /api/internal/audit/refusals</c>: services report refusals into the refused
/// caller's organisation audit log, so the person (or an MCP agent acting for them) can learn why
/// an action was refused without service logs.
/// </summary>
public class InternalRefusalAuditEndpointsTests
    : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private const string Route = "/api/internal/audit/refusals";

    private readonly TenantServiceWebApplicationFactory _factory;

    public InternalRefusalAuditEndpointsTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        await _factory.SeedTestDataAsync();
        await ClearRefusalsAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Report_WithoutServiceToken_IsRefused()
    {
        var response = await _factory.CreateMemberClient().PostAsJsonAsync(Route, Report());

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
        (await RefusalsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Report_RecordsAPermissionDeniedEntryInTheCallersOrganisation()
    {
        var response = await ServiceClient("wallet-service").PostAsJsonAsync(Route, Report());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var entry = (await RefusalsAsync()).Should().ContainSingle().Subject;
        entry.EventType.Should().Be(AuditEventType.PermissionDenied);
        entry.Success.Should().BeFalse();
        entry.OrganizationId.Should().Be(TestDataSeeder.TestOrganizationId);
        entry.IdentityId.Should().Be(TestDataSeeder.AdminPlatformUserId);
        Detail(entry, "action").Should().Be(RefusalAuditActions.WalletSign);
        Detail(entry, "resourceType").Should().Be("wallet");
        Detail(entry, "resourceId").Should().Be("ws11qexample");
        Detail(entry, "reason").Should().Be("the caller holds no active signing delegation on this wallet");
    }

    /// <summary>
    /// The writer is recorded from the service TOKEN. A body naming a different service must not
    /// be able to attribute its refusal to someone else.
    /// </summary>
    [Fact]
    public async Task Report_RecordsTheWriterFromTheToken_NotFromTheBody()
    {
        var body = JsonSerializer.SerializeToElement(Report());
        var forged = JsonSerializer.Deserialize<Dictionary<string, object>>(body.GetRawText())!;
        forged["service"] = "forged-service";

        var response = await ServiceClient("wallet-service").PostAsJsonAsync(Route, forged);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        Detail((await RefusalsAsync()).Single(), "service").Should().Be("wallet-service");
    }

    [Fact]
    public async Task Report_ForAnUnknownOrganisation_IsA4xx_NotA500()
    {
        var response = await ServiceClient("wallet-service").PostAsJsonAsync(
            Route, Report() with { OrganizationId = Guid.NewGuid() });

        ((int)response.StatusCode).Should().BeInRange(400, 499,
            "a refusal report is best-effort; a 500 here would read as a platform fault in the writer");
        (await RefusalsAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "a reason")]
    [InlineData("wallet.sign", "")]
    public async Task Report_WithoutActionOrReason_Returns400(string action, string reason)
    {
        var response = await ServiceClient("wallet-service").PostAsJsonAsync(
            Route, Report() with { Action = action, Reason = reason });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Report_WithAnOverlongReason_Returns400()
    {
        var response = await ServiceClient("wallet-service").PostAsJsonAsync(
            Route, Report() with { Reason = new string('x', RefusalAuditReport.MaxReasonLength + 1) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The join the whole feature exists for: a reported refusal is readable through the
    /// organisation's existing audit query, filtered to refusals.
    /// </summary>
    [Fact]
    public async Task ReportedRefusal_IsReadableThroughTheOrganisationAuditQuery()
    {
        (await ServiceClient("blueprint-service").PostAsJsonAsync(Route, Report() with
        {
            Action = RefusalAuditActions.BlueprintPublish,
            ResourceType = "register",
            ResourceId = "2737dbe4dbb347819055e83faf81c239",
            Reason = "caller lacks a publish-governance role on the register"
        })).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var auditor = _factory.CreateClient();
        auditor.DefaultRequestHeaders.Add("X-Test-User-Id", TestDataSeeder.AuditorUserId.ToString());
        auditor.DefaultRequestHeaders.Add("X-Test-Role", "Auditor");
        auditor.DefaultRequestHeaders.Add("X-Test-Organization-Id", TestDataSeeder.TestOrganizationId.ToString());

        var response = await auditor.GetAsync(
            $"/api/organizations/{TestDataSeeder.TestOrganizationId}/audit?eventType=PermissionDenied");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("events").EnumerateArray().ToList();
        events.Should().ContainSingle();
        events[0].GetProperty("success").GetBoolean().Should().BeFalse();
        var details = events[0].GetProperty("details");
        details.GetProperty("action").GetString().Should().Be(RefusalAuditActions.BlueprintPublish);
        details.GetProperty("reason").GetString().Should().Be("caller lacks a publish-governance role on the register");
        details.GetProperty("service").GetString().Should().Be("blueprint-service");
    }

    private static RefusalAuditReport Report() => new()
    {
        OrganizationId = TestDataSeeder.TestOrganizationId,
        PlatformUserId = TestDataSeeder.AdminPlatformUserId,
        Action = RefusalAuditActions.WalletSign,
        ResourceType = "wallet",
        ResourceId = "ws11qexample",
        Reason = "the caller holds no active signing delegation on this wallet"
    };

    private HttpClient ServiceClient(string clientId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Token-Type", "service");
        client.DefaultRequestHeaders.Add("X-Test-Client-Id", clientId);
        return client;
    }

    private static string? Detail(AuditLogEntry entry, string key) =>
        entry.Details is not null && entry.Details.TryGetValue(key, out var value) ? value?.ToString() : null;

    private async Task<List<AuditLogEntry>> RefusalsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return await db.AuditLogEntries
            .Where(e => e.EventType == AuditEventType.PermissionDenied)
            .ToListAsync();
    }

    private async Task ClearRefusalsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        db.AuditLogEntries.RemoveRange(db.AuditLogEntries.Where(e => e.EventType == AuditEventType.PermissionDenied));
        await db.SaveChangesAsync();
    }
}
