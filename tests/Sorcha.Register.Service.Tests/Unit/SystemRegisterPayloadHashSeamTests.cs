// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Blueprint.Models.Canonical;
using Sorcha.Cryptography.Core;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Sorcha.Validator.Core.Validators;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// The join between the system register's blueprint publisher and the Validator's payload-hash rule
/// (issue #1587).
/// </summary>
/// <remarks>
/// <para>
/// Each side was individually correct and nothing verified the join. The producer hashed the
/// RFC-8785 key-sorted canonical form; the Validator re-serialises whatever arrived, preserving
/// input key order. Every real system blueprint is authored unsorted, so the two could never agree,
/// <c>register-creation-v1</c> was refused <c>TX_012</c> on all three attempts, bootstrap aborted,
/// and <c>register-governance-v1</c> — the publication that must land before any other blueprint can
/// be published — was never reached. Deterministic, on every node, behind a healthy startup.
/// </para>
/// <para>
/// The existing <c>SystemRegisterBlueprintTests</c> mock <see cref="Sorcha.Cryptography.Interfaces.IHashProvider"/>
/// to return <c>new byte[32]</c>, so every hash they compare is equal to every other by construction.
/// That is why a green suite carried this for two days. These tests therefore use the REAL hash
/// provider and the REAL <see cref="TransactionValidator"/> — the point is the join, so a stand-in on
/// either side would make the assertion vacuous.
/// </para>
/// </remarks>
public class SystemRegisterPayloadHashSeamTests
{
    /// <summary>
    /// Keys deliberately NOT in sorted order, matching how every blueprint in
    /// <c>blueprints/templates/</c> is actually authored — <c>id</c>, <c>title</c>, <c>description</c>
    /// sorts to <c>actions</c>, <c>description</c>, <c>id</c>. A pre-sorted fixture would pass
    /// against the broken producer and prove nothing.
    /// </summary>
    private const string UnsortedBlueprint =
        """{"id":"register-creation-v1","title":"Register Creation","description":"Seeded","actions":[{"id":1,"title":"Create"}]}""";

    private readonly Mock<IValidatorServiceClient> _validatorClient = new();
    private readonly Mock<ISystemWalletSigningService> _signingService = new();
    private readonly SystemRegisterService _service;

    public SystemRegisterPayloadHashSeamTests()
    {
        var repository = new Mock<IRegisterRepository>();
        RegisterMockHelpers.StubTransactionsByTypeReadThrough(repository);
        var eventPublisher = new Mock<IEventPublisher>();

        repository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Name = SystemRegisterConstants.SystemRegisterName,
                Height = 0,
                Status = Sorcha.Register.Models.Enums.RegisterStatus.Online
            });

        repository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sorcha.Register.Models.TransactionModel>().AsQueryable());

        _signingService
            .Setup(s => s.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSignResult
            {
                Signature = new byte[64],
                PublicKey = new byte[32],
                Algorithm = "ED25519",
                WalletAddress = "system-wallet-addr"
            });

        _service = new SystemRegisterService(
            new Mock<ILogger<SystemRegisterService>>().Object,
            new RegisterManager(repository.Object, eventPublisher.Object),
            new TransactionManager(repository.Object, eventPublisher.Object),
            _validatorClient.Object,
            _signingService.Object,
            // REAL. A mocked hash provider makes every comparison below vacuously true.
            new HashProvider());
    }

    private async Task<TransactionSubmission> PublishAndCaptureAsync(string blueprintJson)
    {
        TransactionSubmission? captured = null;
        _validatorClient
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((s, _) => captured = s)
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                RegisterId = SystemRegisterConstants.SystemRegisterId
            });

        await _service.PublishBlueprintAsync(
            "register-creation-v1",
            JsonSerializer.Deserialize<JsonElement>(blueprintJson),
            "system");

        captured.Should().NotBeNull("the publish path must reach the validator client");
        return captured!;
    }

    /// <summary>
    /// THE regression. The Validator that will judge this submission is the one asked here, so a
    /// producer that hashes anything other than the bytes it transmits fails.
    /// </summary>
    [Fact]
    public async Task PublishBlueprintAsync_SubmissionSatisfiesTheValidatorsOwnPayloadHashRule()
    {
        var submission = await PublishAndCaptureAsync(UnsortedBlueprint);

        var validator = new TransactionValidator(new HashProvider());
        var result = validator.ValidatePayloadHash(submission.Payload, submission.PayloadHash);

        result.IsValid.Should().BeTrue(
            "the system register's publisher and the Validator must agree on the payload bytes — " +
            "they did not (#1587), so seeding died on the first blueprint with TX_012 and " +
            "register-governance-v1 was never published. Errors: {0}",
            string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    /// <summary>
    /// The mechanism, stated directly: what is transmitted must already be canonical, because the
    /// Validator's re-serialisation preserves the order it receives. This is what the per-register
    /// publish path in <c>Program.cs</c> has always done; the system-register path did not.
    /// </summary>
    [Fact]
    public async Task PublishBlueprintAsync_TransmitsTheCanonicalDefinition_NotTheAuthoredKeyOrder()
    {
        var submission = await PublishAndCaptureAsync(UnsortedBlueprint);

        var canonical = BlueprintCanonicalJson.Canonicalise(UnsortedBlueprint);
        var transmitted = JsonSerializer.Serialize(submission.Payload, CanonicalWireOptions);

        // Compared as a boolean, not via .Be(canonical): FluentAssertions runs its failure message
        // through String.Format, and a JSON subject is full of braces — a real regression would
        // surface as a FormatException rather than as the mismatch it is. The hash-level diff is on
        // the sibling test above.
        string.Equals(transmitted, canonical, StringComparison.Ordinal).Should().BeTrue(
            "the payload on the wire IS the canonical form — that is what makes the transaction id " +
            "self-anchoring, and what lets the Validator's re-serialisation reproduce the hash");
    }

    /// <summary>
    /// The pin must not move. Feature 195's publication id is the digest of the canonical definition,
    /// and recovery re-derives it from the bytes it received; changing which bytes are hashed for the
    /// PAYLOAD must leave the IDENTITY alone.
    /// </summary>
    [Fact]
    public async Task PublishBlueprintAsync_PublicationIdIsStillTheCanonicalDefinitionDigest()
    {
        var submission = await PublishAndCaptureAsync(UnsortedBlueprint);

        submission.TransactionId.Should().Be(
            BlueprintPublicationId.ComputeFromDefinition(
                SystemRegisterConstants.SystemRegisterId, "register-creation-v1", UnsortedBlueprint),
            "recovery recomputes the publication id from the definition it receives and compares it " +
            "to the transaction's own id");
    }

    /// <summary>
    /// Order-independence of the identity, which is the whole point of canonicalising: the same
    /// definition authored two ways is one ledger fact, and both are publishable.
    /// </summary>
    [Fact]
    public async Task PublishBlueprintAsync_ReorderedDefinition_YieldsTheSameIdAndStillValidates()
    {
        const string reordered =
            """{"actions":[{"title":"Create","id":1}],"description":"Seeded","title":"Register Creation","id":"register-creation-v1"}""";

        var first = await PublishAndCaptureAsync(UnsortedBlueprint);
        var second = await PublishAndCaptureAsync(reordered);

        second.TransactionId.Should().Be(first.TransactionId,
            "key order is not part of a definition's content");
        second.PayloadHash.Should().Be(first.PayloadHash,
            "the payload hash is over the canonical bytes, which are order-independent");

        var validator = new TransactionValidator(new HashProvider());
        validator.ValidatePayloadHash(second.Payload, second.PayloadHash).IsValid
            .Should().BeTrue();
    }

    /// <summary>
    /// The options the Validator uses. Restated here rather than shared: this test's job is to hold
    /// the producer to the Validator's rule, so it must not read that rule from the producer.
    /// </summary>
    private static readonly JsonSerializerOptions CanonicalWireOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
