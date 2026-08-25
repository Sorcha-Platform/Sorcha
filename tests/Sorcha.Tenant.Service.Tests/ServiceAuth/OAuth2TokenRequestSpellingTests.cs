// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Tests.ServiceAuth;

/// <summary>
/// Issue #1443 — the token endpoints accept both form-urlencoded and JSON, and the two disagreed on
/// field names: the form path reads the OAuth2 spelling (<c>grant_type</c>), while the JSON path
/// bound the default web camelCase policy (<c>grantType</c>). So the spelling any caller reaches for
/// first bound <b>nothing</b>.
/// </summary>
/// <remarks>
/// The failure was not "field ignored". Every field came back empty, so the request fell through to
/// whichever guard fired first — a valid snake_case <c>client_credentials</c> body against a node
/// with <c>ServiceAuth:DisableSharedSecrets=true</c> answered <i>"Client ID is required"</i> when the
/// truthful answer was <i>"this deployment has retired shared secrets"</i>. That sends an operator to
/// debug their request instead of their credential class.
/// </remarks>
public class OAuth2TokenRequestSpellingTests
{
    // The endpoints deserialize with the request pipeline's web defaults, so the test must too —
    // asserting against SorchaJson or default options would test a policy the endpoint does not use.
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static OAuth2TokenRequest Parse(string json) =>
        JsonSerializer.Deserialize<OAuth2TokenRequest>(json, WebOptions)!;

    [Fact]
    public void OAuth2SnakeCaseSpelling_Binds()
    {
        var r = Parse("""
            {"grant_type":"client_credentials","client_id":"service-blueprint","client_secret":"s3cret"}
            """);

        r.ResolvedGrantType.Should().Be("client_credentials");
        r.ResolvedClientId.Should().Be("service-blueprint");
        r.ResolvedClientSecret.Should().Be("s3cret");
    }

    [Fact]
    public void CamelCaseSpelling_StillBinds()
    {
        // Back-compat is the reason both spellings are accepted rather than swapping to the OAuth2
        // one: in-tree callers and the deploy runbooks already post camelCase JSON.
        var r = Parse("""
            {"grantType":"client_credentials","clientId":"service-blueprint","clientSecret":"s3cret"}
            """);

        r.ResolvedGrantType.Should().Be("client_credentials");
        r.ResolvedClientId.Should().Be("service-blueprint");
        r.ResolvedClientSecret.Should().Be("s3cret");
    }

    [Fact]
    public void RefreshTokenGrant_BindsInBothSpellings()
    {
        Parse("""{"grant_type":"refresh_token","refresh_token":"abc"}""")
            .ResolvedRefreshToken.Should().Be("abc");

        Parse("""{"grantType":"refresh_token","refreshToken":"abc"}""")
            .ResolvedRefreshToken.Should().Be("abc");
    }

    [Fact]
    public void WhenBothSpellingsArePresent_CamelCaseWins()
    {
        // An arbitrary but FIXED precedence. A caller sending both is already confused; what matters
        // is that the answer is deterministic rather than dependent on property declaration order.
        var r = Parse("""{"grantType":"password","grant_type":"client_credentials"}""");

        r.ResolvedGrantType.Should().Be("password");
    }

    [Fact]
    public void AbsentFields_ResolveToEmpty_NotNull()
    {
        // The endpoints assign these into non-nullable locals and then compare them by value; a null
        // here would move the failure from a clean validation problem to a NullReferenceException.
        var r = Parse("{}");

        r.ResolvedGrantType.Should().BeEmpty();
        r.ResolvedClientId.Should().BeEmpty();
        r.ResolvedClientSecret.Should().BeEmpty();
        r.ResolvedRefreshToken.Should().BeEmpty();
    }

    [Fact]
    public void FieldsSpelledIdenticallyInBothConventions_NeedNoAlias()
    {
        // username, password and scope carry no underscore, so they are the same word either way.
        // Asserting it stops someone "completing the set" with aliases that cannot differ.
        var r = Parse("""{"username":"alice","password":"pw","scope":"a b"}""");

        r.Username.Should().Be("alice");
        r.Password.Should().Be("pw");
        r.Scope.Should().Be("a b");
    }
}
