// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// <c>GET /api/registers/{registerId}/blueprints/published</c> — the recovery/discovery read the
/// Blueprint Service rebuilds its published index from (issue #1587, second defect).
/// </summary>
/// <remarks>
/// <para>
/// The endpoint served EVERY <see cref="TransactionType.Control"/> transaction carrying a non-empty
/// <c>MetaData.BlueprintId</c> as a blueprint publication. Its own comment said the pre-#876 arm
/// ought to be gated on <c>TrackingData["transactionType"] == "BlueprintPublish"</c> — the gate was
/// described and never applied.
/// </para>
/// <para>
/// Every governance transaction, and every crypto-policy update, carries
/// <c>BlueprintId = register-governance-v1</c> — because control transactions are genuinely built
/// against the governance workflow. So a DevMode→Normal promotion was served as a phantom
/// publication whose payload is a control record, and <c>BlueprintRecoveryService</c> refused it
/// <c>hash_mismatch</c> on every sweep — hundreds of log lines per node.
/// </para>
/// <para>
/// It was harmless ONLY because Feature 195's provenance check refuses it. Before that check existed
/// the same response would have recovered a control record's payload into the published blueprint
/// store as a definition. This is the #1515 shape at a different reader, and the fix is to use the
/// one predicate written for #1515 rather than a second, weaker rule under the same name.
/// </para>
/// </remarks>
[Collection("RegisterWebApp")]
public class PublishedBlueprintsEndpointTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private const string GovernanceBlueprintId = "register-governance-v1";

    private readonly RegisterServiceWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _registerId;

    public PublishedBlueprintsEndpointTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _registerId = factory
            .CreateTestRegisterAsync("Published Blueprints Test Register", "published-bp-tenant")
            .Result.Id;
    }

    /// <summary>
    /// THE regression. The register below is exactly the shape the EncryptionAtRest walkthrough
    /// leaves behind: one real publication, then a promotion and some governance, all three naming
    /// the governance blueprint.
    /// </summary>
    [Fact]
    public async Task GetPublishedBlueprints_ControlTrafficNamingABlueprint_IsNotServedAsAPublication()
    {
        var publicationTxId = await SeedAsync(BlueprintPublication("worked-example-v1"));
        await SeedAsync(CryptoPolicyUpdate());
        await SeedAsync(GovernanceApproval());

        var blueprints = await GetPublishedAsync();

        blueprints.Select(b => b.GetProperty("blueprintId").GetString())
            .Should().BeEquivalentTo(new[] { "worked-example-v1" },
                "a control transaction NAMES the governance blueprint; it does not publish it. " +
                "Serving one as a publication makes BlueprintRecoveryService refuse a control " +
                "record's payload as a blueprint, hundreds of times per node");

        blueprints.Single().GetProperty("transactionId").GetString().Should().Be(publicationTxId);
    }

    /// <summary>
    /// The other half, and the one that stops the fix over-correcting: a genuine publication must
    /// still be served, under both the post-#876 dedicated type and the pre-#876 marker.
    /// </summary>
    [Theory]
    [InlineData(TransactionType.BlueprintPublish, "BlueprintPublish")]
    [InlineData(TransactionType.Control, "BlueprintPublish")]
    public async Task GetPublishedBlueprints_GenuinePublication_IsServedInBothEras(
        TransactionType persistedType, string marker)
    {
        var id = $"era-{persistedType}-v1".ToLowerInvariant();

        var tx = BlueprintPublication(id);
        tx.MetaData!.TransactionType = persistedType;
        tx.MetaData.TrackingData!["transactionType"] = marker;
        await SeedAsync(tx);

        var blueprints = await GetPublishedAsync();

        var served = blueprints.Single(b => b.GetProperty("blueprintId").GetString() == id);
        JsonDocument.Parse(served.GetProperty("blueprintJson").GetString()!)
            .RootElement.GetProperty("actions").GetArrayLength()
            .Should().BeGreaterThan(0, "the payload served must be the definition itself");
    }

    /// <summary>
    /// The genesis control transaction names no blueprint anyone publishes and must never appear.
    /// Held separately because it is the one exclusion the endpoint always had.
    /// </summary>
    [Fact]
    public async Task GetPublishedBlueprints_Genesis_IsNotServed()
    {
        var genesis = BlueprintPublication("genesis");
        genesis.MetaData!.TransactionType = TransactionType.Control;
        await SeedAsync(genesis);

        var blueprints = await GetPublishedAsync();

        blueprints.Select(b => b.GetProperty("blueprintId").GetString())
            .Should().NotContain("genesis");
    }

    // ------------------------------------------------------------------ //
    // Fixtures                                                            //
    // ------------------------------------------------------------------ //

    private async Task<IReadOnlyList<JsonElement>> GetPublishedAsync()
    {
        var response = await _client.GetAsync($"/api/registers/{_registerId}/blueprints/published");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("blueprints").EnumerateArray().ToList();
    }

    private async Task<string> SeedAsync(TransactionModel tx)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRegisterRepository>();
        tx.RegisterId = _registerId;
        tx.MetaData!.RegisterId = _registerId;
        await repository.InsertTransactionAsync(tx);
        return tx.TxId;
    }

    /// <summary>A genuine publication, as the publish endpoint writes it.</summary>
    private TransactionModel BlueprintPublication(string blueprintId) => Transaction(
        blueprintId,
        TransactionType.BlueprintPublish,
        actionId: null,
        payloadJson: $$"""{"id":"{{blueprintId}}","title":"Worked Example","actions":[{"id":1,"title":"Start"}]}""",
        trackingData: new Dictionary<string, string>
        {
            ["Type"] = "BlueprintPublish",
            ["transactionType"] = "BlueprintPublish",
            ["publishedBy"] = "system"
        });

    /// <summary>
    /// A DevMode→Normal promotion, as <c>CryptoPolicyService</c> writes it — the transaction that
    /// was actually being served as a phantom publication on n1 and tiny.
    /// </summary>
    private TransactionModel CryptoPolicyUpdate() => Transaction(
        GovernanceBlueprintId,
        TransactionType.Control,
        actionId: 100u,
        payloadJson: """{"version":2,"mode":"Normal","registerId":"aebf26362e079087571ac0932d4db973"}""",
        trackingData: new Dictionary<string, string>
        {
            ["Type"] = "Control",
            ["transactionType"] = "CryptoPolicyUpdate",
            ["policyVersion"] = "2"
        });

    /// <summary>A governance approval — an action submission against the governance workflow.</summary>
    private TransactionModel GovernanceApproval() => Transaction(
        GovernanceBlueprintId,
        TransactionType.Control,
        actionId: 2u,
        payloadJson: """{"registerId":"aebf26362e079087571ac0932d4db973","approverDid":"did:sorcha:w:x"}""",
        trackingData: new Dictionary<string, string>
        {
            ["Type"] = "Control",
            ["transactionType"] = "GovernanceApproval"
        });

    private TransactionModel Transaction(
        string blueprintId,
        TransactionType persistedType,
        uint? actionId,
        string payloadJson,
        Dictionary<string, string> trackingData) => new()
        {
            // Unique per seeded transaction — these tests share one register with each other.
            TxId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes($"{blueprintId}|{actionId}|{Guid.NewGuid()}"))).ToLowerInvariant(),
            RegisterId = _registerId,
            SenderWallet = "system",
            RecipientsWallets = new[] { "system" },
            TimeStamp = DateTime.UtcNow,
            MetaData = new TransactionMetaData
            {
                RegisterId = _registerId,
                TransactionType = persistedType,
                BlueprintId = blueprintId,
                ActionId = actionId,
                TrackingData = trackingData
            },
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    WalletAccess = new[] { "system" },
                    PayloadSize = (ulong)payloadJson.Length,
                    Hash = "fakehash",
                    ContentType = "application/json",
                    ContentEncoding = "base64url",
                    Data = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson))
                }
            },
            Signature = "system-signature"
        };
}
