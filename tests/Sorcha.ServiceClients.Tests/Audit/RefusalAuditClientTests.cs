// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceClients.Audit;
using Sorcha.ServiceClients.Auth;

namespace Sorcha.ServiceClients.Tests.Audit;

/// <summary>
/// #1648 — the refusal reporter. It sits on refusal paths in other services, so it must never
/// throw and never turn a correct 403 into a 500.
/// </summary>
public class RefusalAuditClientTests
{
    private static readonly RefusalAuditReport Report = new()
    {
        OrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        PlatformUserId = Guid.Parse("00000000-0000-0001-0000-000000000001"),
        Action = RefusalAuditActions.WalletSign,
        ResourceType = "wallet",
        ResourceId = "ws11qexample",
        Reason = "the caller holds no active signing delegation on this wallet"
    };

    [Fact]
    public async Task RecordAsync_PostsTheReportToTheInternalRefusalEndpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);

        var accepted = await CreateClient(handler).RecordAsync(Report);

        accepted.Should().BeTrue();
        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/internal/audit/refusals");

        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("organizationId").GetGuid().Should().Be(Report.OrganizationId);
        body.RootElement.GetProperty("platformUserId").GetGuid().Should().Be(Report.PlatformUserId!.Value);
        body.RootElement.GetProperty("action").GetString().Should().Be(RefusalAuditActions.WalletSign);
        body.RootElement.GetProperty("reason").GetString().Should().Be(Report.Reason);
    }

    [Fact]
    public async Task RecordAsync_WhenTenantRejects_ReturnsFalseAndDoesNotThrow()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound);

        var act = () => CreateClient(handler).RecordAsync(Report);

        (await act.Should().NotThrowAsync()).Subject.Should().BeFalse();
    }

    [Fact]
    public async Task RecordAsync_WhenTenantIsUnreachable_ReturnsFalseAndDoesNotThrow()
    {
        var handler = new RecordingHandler(new HttpRequestException("connection refused"));

        var act = () => CreateClient(handler).RecordAsync(Report);

        (await act.Should().NotThrowAsync()).Subject.Should().BeFalse();
    }

    /// <summary>A hung Tenant Service must not hold the refused request open.</summary>
    [Fact]
    public async Task RecordAsync_WhenTenantHangs_GivesUpQuicklyWithoutThrowing()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted, delay: TimeSpan.FromSeconds(30));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var accepted = await CreateClient(handler).RecordAsync(Report);

        stopwatch.Stop();
        accepted.Should().BeFalse();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    private static RefusalAuditClient CreateClient(HttpMessageHandler handler)
    {
        var auth = new Mock<IServiceAuthClient>();
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("service-token");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:TenantService:Address"] = "http://tenant-service"
            })
            .Build();

        return new RefusalAuditClient(
            new HttpClient(handler), auth.Object, config, NullLogger<RefusalAuditClient>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly Exception? _throw;
        private readonly TimeSpan _delay;

        public RecordingHandler(HttpStatusCode status, TimeSpan delay = default)
        {
            _status = status;
            _delay = delay;
        }

        public RecordingHandler(Exception toThrow) => _throw = toThrow;

        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            if (_throw is not null)
            {
                throw _throw;
            }

            return new HttpResponseMessage(_status);
        }
    }
}
