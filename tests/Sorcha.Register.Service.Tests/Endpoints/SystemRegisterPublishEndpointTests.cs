// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.Audit;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// <c>POST /api/system-register/blueprints/{id}/publish</c> (Feature 197 T020): SystemAdmin on the platform
/// tier only; every refusal, including policy 403s the handler never sees, is audited (SC-004).
/// </summary>
[Collection("RegisterWebApp")]
public class SystemRegisterPublishEndpointTests : IClassFixture<SystemRegisterPublishWebApplicationFactory>
{
    private const string Url = "/api/system-register/blueprints/register-creation-v1/publish";
    private readonly SystemRegisterPublishWebApplicationFactory _factory;

    public SystemRegisterPublishEndpointTests(SystemRegisterPublishWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    private HttpClient ClientAs(string principal)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DriftTestAuthHandler.PrincipalHeader, principal);
        return client;
    }

    private static PublishDecision Decision(
        SystemBlueprintPublishOutcome outcome, string? txId = null, string reason = "because")
        => new(outcome, SystemBlueprintDriftState.ImageAhead, "tx-current", "tx-candidate", txId, reason);

    [Fact]
    public async Task Publish_SystemAdminOnConsumerTier_Returns403AndIsAudited()
    {
        var response = await ClientAs("sysadmin-consumer").PostAsync(Url, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Publisher.Calls.Should().Be(0);
        var report = _factory.Audit.Reports.Should().ContainSingle().Subject;
        report.Action.Should().Be(RefusalAuditActions.SystemBlueprintPublish);
        report.OrganizationId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        report.ResourceId.Should().Be("register-creation-v1");
    }

    [Fact]
    public async Task Publish_OrgAdministrator_Returns403AndIsAudited()
    {
        var response = await ClientAs("org-admin").PostAsync(Url, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Publisher.Calls.Should().Be(0);
        var report = _factory.Audit.Reports.Should().ContainSingle().Subject;
        report.Action.Should().Be(RefusalAuditActions.SystemBlueprintPublish);
        report.OrganizationId.Should().Be(Guid.Parse(DriftTestAuthHandler.OrgAdminOrg));
    }

    [Fact]
    public async Task Publish_Anonymous_Returns401AndIsNotAudited()
    {
        var response = await ClientAs("anonymous").PostAsync(Url, null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.Audit.Reports.Should().BeEmpty();
    }

    /// <summary>Proves the audit is metadata-gated: the same 403 on an unmarked endpoint is not reported.</summary>
    [Fact]
    public async Task UnmarkedEndpoint_PolicyForbidden_IsNotAudited()
    {
        var response = await ClientAs("sysadmin-consumer").GetAsync("/api/system-register/drift");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.Audit.Reports.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_NoPublishingKey_Returns403ProblemAndIsAudited()
    {
        _factory.Publisher.Next = Decision(SystemBlueprintPublishOutcome.NoPublishingKey, reason: "no key here");

        var response = await ClientAs("sysadmin-platform").PostAsync(Url, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Problem(response)).GetProperty("reason").GetString().Should().Be("no-publishing-key");
        _factory.Audit.Reports.Should().ContainSingle().Which.Reason.Should().Be("no key here");
    }

    [Theory]
    [InlineData(SystemBlueprintPublishOutcome.RefusedRollback, "rollback")]
    [InlineData(SystemBlueprintPublishOutcome.RefusedConcurrency, "concurrency")]
    public async Task Publish_Refused_Returns409WithReasonAndIsAudited(
        SystemBlueprintPublishOutcome outcome, string reason)
    {
        _factory.Publisher.Next = Decision(outcome);

        var response = await ClientAs("sysadmin-platform").PostAsync(Url, null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Problem(response)).GetProperty("reason").GetString().Should().Be(reason);
        _factory.Audit.Reports.Should().ContainSingle()
            .Which.Action.Should().Be(RefusalAuditActions.SystemBlueprintPublish);
    }

    [Fact]
    public async Task Publish_StateUnknown_Returns503RetryableAndIsAudited()
    {
        _factory.Publisher.Next = Decision(SystemBlueprintPublishOutcome.StateUnknown);

        var response = await ClientAs("sysadmin-platform").PostAsync(Url, null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.Should().NotBeNull();
        (await Problem(response)).GetProperty("reason").GetString().Should().Be("state-unknown");
        _factory.Audit.Reports.Should().ContainSingle();
    }

    [Fact]
    public async Task Publish_NotFound_Returns404()
    {
        _factory.Publisher.Next = Decision(SystemBlueprintPublishOutcome.NotFound);

        var response = await ClientAs("sysadmin-platform").PostAsync(Url, null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Publish_DryRun_Returns200AndPassesTheFlagThrough()
    {
        _factory.Publisher.Next = Decision(SystemBlueprintPublishOutcome.DryRun);

        var response = await ClientAs("sysadmin-platform").PostAsJsonAsync(
            Url, new { dryRun = true, expectedCurrent = "tx-current" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        body.GetProperty("outcome").GetString().Should().Be("dry-run");
        body.GetProperty("state").GetString().Should().Be("image-ahead");
        body.GetProperty("transactionId").ValueKind.Should().Be(JsonValueKind.Null);
        _factory.Publisher.LastDryRun.Should().BeTrue();
        _factory.Publisher.LastExpectedCurrent.Should().Be("tx-current");
        _factory.Audit.Reports.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_Submitted_Returns202WithTransactionId()
    {
        _factory.Publisher.Next = Decision(SystemBlueprintPublishOutcome.Submitted, txId: "tx-new");

        var response = await ClientAs("sysadmin-platform").PostAsync(Url, null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        body.GetProperty("outcome").GetString().Should().Be("submitted");
        body.GetProperty("transactionId").GetString().Should().Be("tx-new");
        body.GetProperty("currentPublicationTxId").GetString().Should().Be("tx-current");
        _factory.Publisher.LastDryRun.Should().BeFalse();
        _factory.Audit.Reports.Should().BeEmpty();
    }

    [Fact]
    public async Task Publish_ValidatorRejection_Returns502SanitizedAndIsAudited()
    {
        _factory.Publisher.Throw = new ValidatorRejectedSubmissionException("secret validator internals");

        var response = await ClientAs("sysadmin-platform").PostAsync(Url, null);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("secret validator internals");
        JsonSerializer.Deserialize<JsonElement>(raw).GetProperty("title").GetString()
            .Should().Be("publish submission rejected");
        _factory.Audit.Reports.Should().ContainSingle()
            .Which.Reason.Should().Be("validator rejected the submission");
    }

    /// <summary>A signing or canonicalisation failure is also an InvalidOperationException; it is not a validator rejection.</summary>
    [Fact]
    public async Task Publish_PlainInvalidOperation_Returns500NotAuditedAndNotCountedAsRejected()
    {
        _factory.Publisher.Throw = new InvalidOperationException("signing wallet unavailable");
        var rejected = 0;
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (i, l) =>
        {
            if (i.Name == SystemBlueprintMetrics.PublishCounterName) l.EnableMeasurementEvents(i);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var t in tags)
                if (t.Key == "outcome" && Equals(t.Value, PublishOutcomeNames.Rejected)) Interlocked.Increment(ref rejected);
        });
        listener.Start();

        var response = await ClientAs("sysadmin-platform").PostAsync(Url, null);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("signing wallet unavailable");
        _factory.Audit.Reports.Should().BeEmpty();
        rejected.Should().Be(0);
    }

    [Fact]
    public async Task Publish_OtherException_IsNotMappedTo502()
    {
        _factory.Publisher.Throw = new NullReferenceException("boom internals");

        var response = await ClientAs("sysadmin-platform").PostAsync(Url, null);

        response.StatusCode.Should().NotBe(HttpStatusCode.BadGateway);
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("boom internals");
        _factory.Audit.Reports.Should().BeEmpty();
    }

    private static async Task<JsonElement> Problem(HttpResponseMessage response)
        => JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
}

/// <summary>Drift-test host (header-driven principals) with fake publish service and refusal audit client.</summary>
public class SystemRegisterPublishWebApplicationFactory : SystemRegisterDriftWebApplicationFactory
{
    internal FakePublisher Publisher { get; } = new();

    internal FakeAudit Audit { get; } = new();

    internal void Reset()
    {
        Publisher.Next = null;
        Publisher.Throw = null;
        Publisher.Calls = 0;
        Publisher.LastDryRun = false;
        Publisher.LastExpectedCurrent = null;
        Audit.Reports.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISystemBlueprintPublishService>();
            services.AddSingleton<ISystemBlueprintPublishService>(Publisher);
            services.RemoveAll<IRefusalAuditClient>();
            services.AddSingleton<IRefusalAuditClient>(Audit);
        });
    }

    internal sealed class FakePublisher : ISystemBlueprintPublishService
    {
        public PublishDecision? Next { get; set; }
        public Exception? Throw { get; set; }
        public int Calls { get; set; }
        public bool LastDryRun { get; set; }
        public string? LastExpectedCurrent { get; set; }

        public Task<PublishDecision> PublishAsync(
            string blueprintId, bool dryRun, string? expectedCurrent, string operatorId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastDryRun = dryRun;
            LastExpectedCurrent = expectedCurrent;
            if (Throw is not null)
            {
                throw Throw;
            }

            return Task.FromResult(Next ?? throw new InvalidOperationException("no decision configured"));
        }
    }

    internal sealed class FakeAudit : IRefusalAuditClient
    {
        public List<RefusalAuditReport> Reports { get; } = [];

        public Task<bool> RecordAsync(RefusalAuditReport report, CancellationToken cancellationToken = default)
        {
            Reports.Add(report);
            return Task.FromResult(true);
        }
    }
}
