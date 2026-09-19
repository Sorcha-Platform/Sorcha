// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Sorcha.Register.Models;
using Sorcha.Register.Service.Tests.Helpers;

using Xunit;

namespace Sorcha.Register.Service.Tests;

/// <summary>
/// #1669 — a transaction the validator refused after its 202 must be discoverable by whoever
/// submitted it.
/// </summary>
/// <remarks>
/// <para>
/// Submission is asynchronous by design: the API answers 202 and validation happens afterwards.
/// A rejection produced no error, no observable status change and nothing to query — the operation
/// simply never happened, and the only evidence anywhere was a WRN line in the validator's
/// container log. Cold-start run #5 lost a participant revocation to <c>VAL_CHAIN_FORK</c> that
/// way, while the 202 body read <c>"status":"submitted"</c>.
/// </para>
/// <para>
/// A rejected transaction is absent from the chain, which is why the status endpoint's 404 path is
/// where the answer belongs.
/// </para>
/// </remarks>
[Collection("RegisterWebApp")]
public class TransactionRejectionStatusTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private const string RegisterId = "test-register-rejections";
    private const string TxId = "abe4e5f71553a670e3ea01e0caa60c265052686f5c444cb221e56a1f235e0a00";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly RegisterServiceWebApplicationFactory _factory;

    public TransactionRejectionStatusTests(RegisterServiceWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Status_ForARejectedTransaction_ReportsTheRejectionAndItsReason()
    {
        var log = new StubRejectionLog(new TransactionRejection(
            TxId, RegisterId, "VAL_CHAIN_FORK",
            "Fork detected: 1 existing transaction(s) already reference previous transaction 'd20cfff7'",
            DateTimeOffset.Parse("2026-09-19T11:11:54Z")));

        var response = await ClientWith(log).GetAsync($"/api/registers/{RegisterId}/transactions/{TxId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), JsonOptions);

        // On the wire as a STRING: Register calls SorchaJson.Configure on its HTTP JSON options
        // (Program.cs), so its enums are named rather than the bare integers CLAUDE.md §25 warns
        // about elsewhere. Asserted against the real bytes, because guessing this wrong is the
        // whole of #1613.
        // The literal, not nameof(): "rejected" IS the wire contract a client matches on, and
        // SorchaJson names enums in camelCase. Deriving it from the C# name would let a rename
        // move the wire value silently.
        json.GetProperty("status").GetString().Should().Be("rejected");
        json.GetProperty("rejectionCode").GetString().Should().Be("VAL_CHAIN_FORK");
        json.GetProperty("rejectionReason").GetString().Should().Contain("Fork detected");
        json.GetProperty("transactionId").GetString().Should().Be(TxId);
    }

    [Fact]
    public async Task Status_ForAnUnknownTransaction_StaysA404AndSaysItIsNotEvidenceOfAcceptance()
    {
        // The dangerous reading. A rejection record can expire and a transaction may never have
        // reached this node, so "no record" must not be reported as, or mistaken for, success.
        var response = await ClientWith(new StubRejectionLog(null))
            .GetAsync($"/api/registers/{RegisterId}/transactions/{TxId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("not evidence");
    }

    private HttpClient ClientWith(ITransactionRejectionLog log) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITransactionRejectionLog>();
                services.AddSingleton(log);
            })).CreateClient();

    private sealed class StubRejectionLog(TransactionRejection? rejection) : ITransactionRejectionLog
    {
        public Task RecordAsync(TransactionRejection r, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<TransactionRejection?> FindAsync(
            string registerId, string transactionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(rejection);
    }
}
