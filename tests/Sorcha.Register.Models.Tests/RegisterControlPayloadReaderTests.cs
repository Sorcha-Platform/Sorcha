// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Xunit;

namespace Sorcha.Register.Models.Tests;

public class RegisterControlPayloadReaderTests
{
    [Fact]
    public void TryRead_WrapperShape_PopulatedRoster_ReturnsRoster()
    {
        var inner = MakeRecord(withAttestation: true, withValidator: false);
        var wrapper = new ControlTransactionPayload
        {
            Version = 1,
            Roster = inner,
            Operation = null,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(wrapper);

        var result = RegisterControlPayloadReader.TryRead(bytes);

        result.Should().NotBeNull();
        result!.Attestations.Should().HaveCount(1);
        result.Attestations[0].Subject.Should().Be("did:sorcha:w:owner1");
    }

    [Fact]
    public void TryRead_WrapperShape_WithOnlyValidatorRoster_StillReturnsRoster()
    {
        // A valid governance snapshot may carry the validator set without any participant
        // attestations — the reader should still surface it (not treat it as empty).
        var inner = MakeRecord(withAttestation: false, withValidator: true);
        var wrapper = new ControlTransactionPayload { Version = 1, Roster = inner, Operation = null };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(wrapper);

        var result = RegisterControlPayloadReader.TryRead(bytes);

        result.Should().NotBeNull();
        result!.Validators!.Validators.Should().HaveCount(1);
    }

    [Fact]
    public void TryRead_BareShape_PopulatedRoster_ReturnsRoster()
    {
        // Pre-#868 genesis bytes — bare RegisterControlRecord on disk. The reader must keep
        // accepting these so a long-lived register's docket-0 control tx still derives roles
        // after the writer flip.
        var record = MakeRecord(withAttestation: true, withValidator: false);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);

        var result = RegisterControlPayloadReader.TryRead(bytes);

        result.Should().NotBeNull();
        result!.Attestations.Should().HaveCount(1);
    }

    [Fact]
    public void TryRead_EmptyBytes_ReturnsNull()
    {
        RegisterControlPayloadReader.TryRead(ReadOnlySpan<byte>.Empty).Should().BeNull();
    }

    [Fact]
    public void TryRead_NonControlJson_ReturnsNull()
    {
        // An action-submission JSON blob — neither shape carries attestations or validators,
        // so a silent default-empty deserialisation must not poison downstream callers.
        // This is the exact symptom that drove PR #871 on the projection side.
        var bytes = Encoding.UTF8.GetBytes("{\"action\":1,\"applicantName\":\"Alex MacLeod\"}");

        RegisterControlPayloadReader.TryRead(bytes).Should().BeNull();
    }

    [Fact]
    public void TryRead_MalformedJson_ReturnsNull()
    {
        var bytes = Encoding.UTF8.GetBytes("not-json-at-all");

        RegisterControlPayloadReader.TryRead(bytes).Should().BeNull();
    }

    [Fact]
    public void TryRead_StringOverload_RoundTrips()
    {
        var record = MakeRecord(withAttestation: true, withValidator: false);
        var wrapper = new ControlTransactionPayload { Version = 1, Roster = record };
        var json = JsonSerializer.Serialize(wrapper);

        var result = RegisterControlPayloadReader.TryRead(json);

        result.Should().NotBeNull();
        result!.Attestations[0].Subject.Should().Be("did:sorcha:w:owner1");
    }

    private static RegisterControlRecord MakeRecord(bool withAttestation, bool withValidator)
    {
        var record = new RegisterControlRecord
        {
            RegisterId = "abc",
            Name = "Test Register",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (withAttestation)
        {
            record.Attestations = new List<RegisterAttestation>
            {
                new()
                {
                    Role = RegisterRole.Owner,
                    Subject = "did:sorcha:w:owner1",
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Signature = Convert.ToBase64String(new byte[64]),
                    Algorithm = SignatureAlgorithm.ED25519,
                    GrantedAt = DateTimeOffset.UtcNow,
                },
            };
        }

        if (withValidator)
        {
            record.Validators = new ValidatorRoster
            {
                Validators = new List<ValidatorRosterEntry>
                {
                    new()
                    {
                        ValidatorId = "v1",
                        PublicKey = Convert.ToBase64String(new byte[32]),
                        Algorithm = SignatureAlgorithm.ED25519,
                    },
                },
            };
        }

        return record;
    }
}
