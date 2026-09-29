// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Sorcha.Blueprint.Service.Configuration;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Wallet.Contracts.Models;

using Xunit;

namespace Sorcha.Blueprint.Service.Tests.StatusList;

/// <summary>
/// TODO(095) / #1759 — the IETF status list endpoint used to sign with a configured key or, on every
/// deployment that set none (n1 included), an EPHEMERAL key nobody could verify. It now has the
/// issuing organisation's key sign the list inside the Wallet Service, and serves nothing it cannot
/// have signed that way. A real <see cref="StatusListManager"/> backs these tests so a revocation
/// changes real bits; only the Wallet client — the seam under test — is mocked.
/// </summary>
public class IetfStatusListEndpointTests
{
    private static readonly Guid Org = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private const string IetfBase = "https://n1.sorcha.dev/api/v1/credentials/ietf-status-lists";

    private readonly StatusListManager _manager = NewManager();
    private readonly Mock<IWalletServiceClient> _wallet = new();
    private readonly FixedClock _clock = new(DateTimeOffset.Parse("2026-09-29T10:00:00Z"));
    private readonly IetfStatusListTokenCache _cache;
    private readonly List<SignStatusListTokenRequest> _signed = [];

    public IetfStatusListEndpointTests()
    {
        _cache = new IetfStatusListTokenCache(_clock);
        _wallet.Setup(w => w.SignStatusListTokenAsync(It.IsAny<SignStatusListTokenRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SignStatusListTokenRequest r, CancellationToken _) =>
            {
                _signed.Add(r);
                return new SignStatusListTokenResponse
                {
                    Jwt = $"signed-{_signed.Count}",
                    IssuerDid = "did:sorcha:org:ws1",
                    Kid = "did:sorcha:org:ws1#vc-issuance-0",
                };
            });
    }

    [Fact]
    public async Task Get_ListWithIssuingOrganisation_ServesTheTokenTheOrgKeySigned()
    {
        var alloc = await _manager.AllocateIndexAsync("ws11qsender", "register-1", null, Org);
        await _manager.SetBitAsync(alloc.ListId, alloc.Index, true);

        var (status, contentType, body, cacheControl) = await Get(alloc.ListId);

        status.Should().Be(200);
        contentType.Should().StartWith("application/statuslist+jwt");
        body.Should().Be("signed-1");
        cacheControl.Should().Be("public, max-age=300");

        var request = _signed.Single();
        request.OrganizationId.Should().Be(Org, "the list is signed by the org whose credentials it reports on");
        request.Subject.Should().Be($"{IetfBase}/{alloc.ListId}",
            "sub MUST equal the status_list.uri credentials carry (RFC 9972 §5.1)");
        request.Bits.Should().Be(2, "a suspension list exists, so the view is the 2-bit projection");
        request.TtlSeconds.Should().Be(300);
        var list = (await _manager.GetListAsync(alloc.ListId))!;
        var suspension = (await _manager.GetListAsync(alloc.SuspensionListId))!;
        Convert.FromBase64String(request.EntriesBase64).Should().Equal(
            IetfStatusListPacker.PackTwoBit(list, suspension, list.Size));
    }

    [Fact]
    public async Task Get_ListWithNoIssuingOrganisation_Returns409AndSignsNothing()
    {
        var alloc = await _manager.AllocateIndexAsync("ws11qlegacy", "register-1", null);

        var (status, _, _, _) = await Get(alloc.ListId);

        status.Should().Be(409, "there is no key a verifier could resolve, and no fallback key");
        _signed.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_OrgHasNoIssuanceKey_Returns409()
    {
        var alloc = await _manager.AllocateIndexAsync("ws11qsender", "register-1", null, Org);
        _wallet.Setup(w => w.SignStatusListTokenAsync(It.IsAny<SignStatusListTokenRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SignStatusListTokenResponse?)null);

        var (status, _, _, _) = await Get(alloc.ListId);

        status.Should().Be(409);
    }

    [Fact]
    public async Task Get_SigningFails_Returns503NotAnUnsignedList()
    {
        var alloc = await _manager.AllocateIndexAsync("ws11qsender", "register-1", null, Org);
        _wallet.Setup(w => w.SignStatusListTokenAsync(It.IsAny<SignStatusListTokenRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("wallet down"));

        var (status, _, _, _) = await Get(alloc.ListId);

        status.Should().Be(503);
    }

    [Fact]
    public async Task Get_Twice_WithUnchangedEntries_SignsOnce()
    {
        var alloc = await _manager.AllocateIndexAsync("ws11qsender", "register-1", null, Org);

        await Get(alloc.ListId);
        _clock.Advance(TimeSpan.FromSeconds(100));
        var (_, _, body, cacheControl) = await Get(alloc.ListId);

        body.Should().Be("signed-1");
        _signed.Should().HaveCount(1);
        cacheControl.Should().Be("public, max-age=200",
            "a reused token has only the rest of its own lifetime left, not a fresh max-age");
    }

    [Fact]
    public async Task Get_AfterARevocation_SignsAFreshListImmediately()
    {
        var alloc = await _manager.AllocateIndexAsync("ws11qsender", "register-1", null, Org);
        await Get(alloc.ListId);

        await _manager.SetBitAsync(alloc.ListId, alloc.Index, true);
        var (_, _, body, _) = await Get(alloc.ListId);

        body.Should().Be("signed-2", "a revocation must never wait out a cached signature");
        _signed.Should().HaveCount(2);
    }

    [Fact]
    public async Task Get_PastHalfTheMaxAge_SignsAgain()
    {
        var alloc = await _manager.AllocateIndexAsync("ws11qsender", "register-1", null, Org);
        await Get(alloc.ListId);

        _clock.Advance(TimeSpan.FromSeconds(151));
        var (_, _, body, _) = await Get(alloc.ListId);

        body.Should().Be("signed-2", "a token is reused for at most half its lifetime");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<(int Status, string? ContentType, string Body, string? CacheControl)> Get(string listId)
    {
        var result = await StatusListEndpoints.GetIetfStatusList(
            listId,
            _manager,
            _wallet.Object,
            _cache,
            new ConfigurationBuilderStub(maxAge: 300).Build(),
            new StatusListUrls.Resolved("https://n1.sorcha.dev/api/v1/credentials/status-lists", IetfBase),
            _clock,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return (context.Response.StatusCode, context.Response.ContentType, body,
            context.Response.Headers.CacheControl.ToString() is { Length: > 0 } cc ? cc : null);
    }

    private static StatusListManager NewManager()
    {
        var register = new Mock<IRegisterServiceClient>();
        register.Setup(r => r.GetTransactionsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 100, Total = 0, Transactions = [] });
        var services = new ServiceCollection();
        services.AddScoped(_ => register.Object);
        var reconciler = new StatusListLedgerReconciler(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StatusListLedgerReconciler>.Instance);

        return new StatusListManager(
            NullLogger<StatusListManager>.Instance,
            new StatusListUrls.Resolved("https://n1.sorcha.dev/api/v1/credentials/status-lists", IetfBase),
            new InMemoryStatusListStore(),
            reconciler);
    }

    private sealed class ConfigurationBuilderStub(int maxAge)
    {
        public Microsoft.Extensions.Configuration.IConfiguration Build() =>
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["StatusList:CacheMaxAgeSeconds"] = maxAge.ToString(),
                })
                .Build();
    }

    private sealed class FixedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
