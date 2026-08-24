// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Transaction = Sorcha.Validator.Service.Models.Transaction;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Guards the <see cref="Docket"/> → <c>DocketModel</c> projection — the single join between the
/// validator's consensus working model and the canonical ledger model that gets persisted.
/// </summary>
/// <remarks>
/// <para>
/// This seam has caused three separate silent production defects, each of the same shape: a field
/// present on both sides, absent from the hand-maintained projection, with no error anywhere.
/// <c>InstanceId</c> was dropped (every F145 Tier-3 lookup returned empty); <c>TrackingData</c> was
/// dropped (a recovering node rejected every blueprint with <c>no_provenance</c>); and separately
/// <c>LastAppliedTxId</c> was dropped by <c>EfCoreInstanceStore.UpdateAsync</c>'s copy list (F145's
/// replay guard died and the F142 go-live gate became unearnable).
/// </para>
/// <para>
/// So this test does NOT assert a fixed list of fields — a fixed list is exactly what keeps failing.
/// It reflects over <b>every</b> property of <see cref="TransactionMetaData"/>, so the next property
/// added to that type cannot be silently omitted from the projection.
/// </para>
/// </remarks>
public class DocketProjectionCompletenessTests
{
    /// <summary>
    /// The projection under test — the single implementation, after Feature 187 US1 (#1370) collapsed
    /// <c>DocketBuildTriggerService</c>'s inline block and <c>DocketSerializer.ToRegisterModel</c>
    /// into <see cref="DocketRegisterProjection"/>.
    /// </summary>
    private static DocketModel Project(Docket docket) => DocketRegisterProjection.ToDocketModel(docket);

    [Fact]
    public void Projection_PopulatesEveryTransactionMetaDataProperty()
    {
        // Arrange — a transaction carrying every metadata fact the ledger model can hold.
        var docket = CreateDocketWithFullyPopulatedTransaction();

        // Act
        var model = Project(docket);

        // Assert
        var metaData = model.Transactions.Should().ContainSingle().Which.MetaData;
        metaData.Should().NotBeNull();

        var unpopulated = typeof(TransactionMetaData)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            // TransactionType is a non-nullable enum whose default (Control) is a legitimate value,
            // so "is it populated" is not decidable by reflection. It gets its own explicit test below.
            .Where(p => p.Name != nameof(TransactionMetaData.TransactionType))
            .Where(p => !IsPopulated(p.GetValue(metaData!)))
            .Select(p => p.Name)
            .ToList();

        // Name EVERY dropped field, not just the first — BeEmpty's default message reports only one
        // item, and the whole value of this guard is telling you precisely what the projection lost.
        unpopulated.Should().BeEmpty(
            "every TransactionMetaData property must survive the docket→register projection; " +
            "a field dropped here is written nowhere, errors nowhere, and surfaces far away as " +
            "an instance that never advances or a node that rejects every blueprint. " +
            "Dropped by this projection: [{0}]",
            string.Join(", ", unpopulated));
    }

    [Fact]
    public void Projection_PreservesLifecycleTransactionType()
    {
        // A presentation-lifecycle transaction MUST keep its own type through the seal. Collapsing it
        // to Action makes TransactionTypeClassifier.IsLifecycleTransaction stop matching, so the
        // action-schema exemption is lost and the transaction becomes permanently unsealable
        // (VAL_SCHEMA_004) — Feature 111's trap 2, reintroduced by the mapper.
        var docket = CreateDocketWithFullyPopulatedTransaction();

        var model = Project(docket);

        model.Transactions.Should().ContainSingle()
            .Which.MetaData!.TransactionType.Should().Be(TransactionType.PresentationOutcome);
    }

    [Fact]
    public void Projection_MapsGenesisMetadataTypeToControl()
    {
        // "Genesis" is written by RegisterCreationOrchestrator but is not an enum member; it maps to
        // Control because genesis is the control record that bootstraps the register.
        var docket = CreateDocketWithFullyPopulatedTransaction();
        docket.Transactions[0].Metadata["Type"] = "Genesis";

        var model = Project(docket);

        model.Transactions.Should().ContainSingle()
            .Which.MetaData!.TransactionType.Should().Be(TransactionType.Control);
    }

    /// <summary>
    /// A value counts as populated when it carries information: non-null, non-blank, non-empty.
    /// </summary>
    private static bool IsPopulated(object? value) => value switch
    {
        null => false,
        string s => !string.IsNullOrWhiteSpace(s),
        System.Collections.ICollection c => c.Count > 0,
        _ => true
    };

    private static Docket CreateDocketWithFullyPopulatedTransaction()
    {
        var routingDecision = new RoutingDecision
        {
            CompletedActionId = 3,
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
            NextActions = [new ActionRef { ActionId = 4 }],
            RouteId = "route-approve",
            ReasonCode = "approved"
        };

        var transaction = new Transaction
        {
            TransactionId = "tx-1",
            RegisterId = "register-1",
            BlueprintId = "blueprint-1",
            ActionId = "3",
            Payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"a\":1}"),
            PayloadHash = "hash-tx-1",
            CreatedAt = DateTimeOffset.UtcNow,
            PreviousTransactionId = "tx-0",
            SequenceNumber = 1,
            RecipientsWallets = ["ws1qrecipient"],
            Priority = TransactionPriority.Normal,
            Signatures =
            [
                new RegisterSignature
                {
                    PublicKey = [1, 2, 3],
                    SignatureValue = [4, 5, 6],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow,
                    SignedBy = "ws1qsender"
                }
            ],
            Metadata = new Dictionary<string, string>
            {
                ["Type"] = nameof(TransactionType.PresentationOutcome),
                ["instanceId"] = "instance-1",
                ["routingDecision"] = System.Text.Json.JsonSerializer.Serialize(
                    routingDecision, RegisterSerializationOptions.Canonical)
            }
        };

        return new Docket
        {
            DocketId = "docket-1",
            RegisterId = "register-1",
            DocketNumber = 5,
            DocketHash = "hash-abc",
            PreviousHash = "hash-prev",
            MerkleRoot = "merkle-root",
            CreatedAt = DateTimeOffset.UtcNow,
            ProposerValidatorId = "validator-1",
            Status = DocketStatus.Proposed,
            ProposerSignature = new RegisterSignature
            {
                PublicKey = [1, 2, 3],
                SignatureValue = [4, 5, 6],
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            },
            Transactions = [transaction]
        };
    }
}
