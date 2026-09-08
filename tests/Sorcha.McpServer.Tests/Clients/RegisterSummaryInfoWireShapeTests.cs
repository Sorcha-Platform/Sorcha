// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models.Enums;
using Sorcha.Serialization;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tests.Clients;

/// <summary>
/// Pins <see cref="RegisterSummaryInfo"/> against the shape <c>GET /api/registers/</c> actually
/// sends (#1613).
/// </summary>
/// <remarks>
/// The Register Service registers no JSON options, so its minimal APIs use the web defaults and an
/// enum goes on the wire as a NUMBER. <c>Status</c> was typed as <c>string</c>, so deserializing a
/// real response threw <c>JsonException: Cannot get the value of a token type 'Number' as a
/// string</c> — which <c>GetRecentRegistersAsync</c> caught and turned into an empty list. Both
/// consumers then reported "0 registers" against a node holding five, with no error anywhere.
/// <para>
/// The payload below is a verbatim capture from n1 on 2026-09-07, trimmed to the fields the DTO
/// binds. Do not "tidy" <c>"status": 1</c> into <c>"online"</c> — the integer IS the contract under
/// test, and a fixture that sends a string can only ever pass.
/// </para>
/// </remarks>
public class RegisterSummaryInfoWireShapeTests
{
    private const string LiveResponseJson = """
        [
          {
            "id": "aebf26362e079087571ac0932d4db973",
            "name": "Sorcha System Register",
            "description": "Sorcha platform system register — root of trust.",
            "height": 5,
            "status": 1,
            "advertise": true,
            "isFullReplica": true,
            "purpose": "System",
            "createdAt": "2026-08-29T12:35:41.797Z",
            "updatedAt": "2026-08-29T12:36:10.758Z",
            "devMode": false,
            "syncState": 2
          }
        ]
        """;

    [Fact]
    public void Deserialize_LiveServerResponse_YieldsTheRegisterRatherThanThrowing()
    {
        var registers = JsonSerializer.Deserialize<List<RegisterSummaryInfo>>(
            LiveResponseJson, SorchaJson.Options);

        registers.Should().NotBeNull();
        registers!.Should().ContainSingle(
            "a numeric status must not collapse the whole list to empty — that is #1613");
        registers[0].Id.Should().Be("aebf26362e079087571ac0932d4db973");
        registers[0].Name.Should().Be("Sorcha System Register");
        registers[0].Height.Should().Be(5);
    }

    [Fact]
    public void Deserialize_NumericStatus_MapsToTheNamedEnumValue()
    {
        var registers = JsonSerializer.Deserialize<List<RegisterSummaryInfo>>(
            LiveResponseJson, SorchaJson.Options);

        registers![0].Status.Should().Be(RegisterStatus.Online,
            "the server sends 1, which is Online — reporting the raw number would be no more useful "
            + "to a caller than the failure it replaced");
    }

    [Theory]
    [InlineData(0, RegisterStatus.Offline)]
    [InlineData(1, RegisterStatus.Online)]
    [InlineData(2, RegisterStatus.Checking)]
    public void Deserialize_EveryDeclaredStatusValue_RoundTripsFromItsNumber(
        int wireValue, RegisterStatus expected)
    {
        var json = $$"""[{"id":"r","name":"n","height":0,"status":{{wireValue}},"createdAt":"2026-01-01T00:00:00Z"}]""";

        var registers = JsonSerializer.Deserialize<List<RegisterSummaryInfo>>(json, SorchaJson.Options);

        registers![0].Status.Should().Be(expected);
    }

    [Fact]
    public void RegisterSummaryInfo_DeclaresNoTenantId_BecauseTheEndpointSendsNone()
    {
        // The live capture above has no `tenantId`, and Sorcha.Register.Models.Register — the type
        // GET /api/registers/ serialises — has no tenant or organisation field at all. TenantId was
        // declared here anyway, so it bound nothing and every consumer reported an empty owner for
        // every register: sorcha_register_stats and sorcha://registers both did.
        //
        // Asserted by reflection rather than by a fixture, because a fixture would have to SET the
        // value the server never sends — which is exactly why the old tests passed.
        typeof(RegisterSummaryInfo).GetProperty("TenantId").Should().BeNull(
            "a register is not owned by one tenant in this model — organisations subscribe to "
            + "registers — so there is nothing to populate it from; do not re-add it");
    }

    [Fact]
    public void Deserialize_LiveServerResponse_CarriesNoTenantField()
    {
        using var doc = JsonDocument.Parse(LiveResponseJson);

        doc.RootElement[0].TryGetProperty("tenantId", out _).Should().BeFalse(
            "the capture is verbatim from n1 — if the server ever starts sending this, the DTO "
            + "should gain the property back deliberately rather than by guess");
    }

    [Fact]
    public void Deserialize_StringStatus_StillBinds()
    {
        // SorchaJson registers a kebab-case JsonStringEnumConverter, so a future server that emits
        // names must not break the client the way the numeric form did.
        var json = """[{"id":"r","name":"n","height":0,"status":"online","createdAt":"2026-01-01T00:00:00Z"}]""";

        var registers = JsonSerializer.Deserialize<List<RegisterSummaryInfo>>(json, SorchaJson.Options);

        registers![0].Status.Should().Be(RegisterStatus.Online);
    }
}
