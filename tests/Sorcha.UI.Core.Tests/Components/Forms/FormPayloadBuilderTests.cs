// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// Regression coverage for the Feature 103 v2 nested-payload bug:
/// <c>FormContext</c> stores values keyed by JSON Pointer scopes, and
/// both <c>NewSubmissionWorkspace</c> and <c>MyActions</c> used to submit
/// the flat pointer-keyed dictionary by just stripping the leading slash.
/// Once <c>SchemaRefResolver</c> started flattening core identity
/// primitives (PersonName, DateOfBirth, EmailAddress, PostalAddress) into
/// action schemas as nested objects, that shortcut produced payloads that
/// failed validator schema validation with
/// <c>VAL_SCHEMA_004: Required properties ["name","dob","email","address"] are not present</c>.
/// <see cref="FormPayloadBuilder"/> reconstructs the nested tree so the
/// validator sees the correct shape.
/// </summary>
public class FormPayloadBuilderTests
{
    [Fact]
    public void BuildNested_FlatPointerKeyedDictionary_AssemblesNestedObjects()
    {
        var flat = new Dictionary<string, object?>
        {
            ["/name/givenName"] = "Alice",
            ["/name/familyName"] = "Smith",
            ["/dob/dateOfBirth"] = "1990-03-15",
            ["/email/email"] = "alice@example.com",
            ["/address/line1"] = "42 Grafton Street",
            ["/address/town"] = "Dublin",
            ["/address/country"] = "IE"
        };

        var nested = FormPayloadBuilder.BuildNested(flat);

        nested.Should().ContainKey("name");
        var name = nested["name"].Should().BeOfType<Dictionary<string, object>>().Subject;
        name["givenName"].Should().Be("Alice");
        name["familyName"].Should().Be("Smith");

        var dob = nested["dob"].Should().BeOfType<Dictionary<string, object>>().Subject;
        dob["dateOfBirth"].Should().Be("1990-03-15");

        var email = nested["email"].Should().BeOfType<Dictionary<string, object>>().Subject;
        email["email"].Should().Be("alice@example.com");

        var address = nested["address"].Should().BeOfType<Dictionary<string, object>>().Subject;
        address["line1"].Should().Be("42 Grafton Street");
        address["town"].Should().Be("Dublin");
        address["country"].Should().Be("IE");
    }

    [Fact]
    public void BuildNested_TopLevelScalarValues_AreStoredUnderRoot()
    {
        var flat = new Dictionary<string, object?>
        {
            ["/reference"] = "REF-001",
            ["/count"] = 7
        };

        var nested = FormPayloadBuilder.BuildNested(flat);

        nested["reference"].Should().Be("REF-001");
        nested["count"].Should().Be(7);
    }

    [Fact]
    public void BuildNested_NullValues_AreCoercedToEmptyString()
    {
        // Matches the pre-fix behaviour of `kvp.Value ?? (object)string.Empty`
        // in the submission call sites. Required so the non-nullable
        // Dictionary<string, object> on ActionExecuteRequest.PayloadData
        // can hold every entry.
        var flat = new Dictionary<string, object?>
        {
            ["/name/middleName"] = null,
            ["/address/line2"] = null
        };

        var nested = FormPayloadBuilder.BuildNested(flat);

        var name = nested["name"].Should().BeOfType<Dictionary<string, object>>().Subject;
        name["middleName"].Should().Be(string.Empty);

        var address = nested["address"].Should().BeOfType<Dictionary<string, object>>().Subject;
        address["line2"].Should().Be(string.Empty);
    }

    [Fact]
    public void BuildNested_EmptyKeyAndSlashOnly_AreIgnored()
    {
        var flat = new Dictionary<string, object?>
        {
            [""] = "skip-me",
            ["/"] = "also-skip",
            ["/kept"] = "alive"
        };

        var nested = FormPayloadBuilder.BuildNested(flat);

        nested.Should().HaveCount(1);
        nested["kept"].Should().Be("alive");
    }

    [Fact]
    public void BuildNested_EscapedPointerSegments_AreUnescapedPerRfc6901()
    {
        // RFC 6901 defines ~1 → / and ~0 → ~ in pointer segments. Order
        // matters: ~0 is decoded AFTER ~1 so literal ~1 in the original
        // segment survives the decode.
        var flat = new Dictionary<string, object?>
        {
            ["/weird~1key/inner"] = "value1",
            ["/tilde~0field"] = "value2"
        };

        var nested = FormPayloadBuilder.BuildNested(flat);

        var weird = nested["weird/key"].Should().BeOfType<Dictionary<string, object>>().Subject;
        weird["inner"].Should().Be("value1");

        nested["tilde~field"].Should().Be("value2");
    }

    [Fact]
    public void BuildNested_MultipleWritesToSameObject_BothSurvive()
    {
        var flat = new Dictionary<string, object?>
        {
            ["/name/givenName"] = "A",
            ["/name/familyName"] = "B"
        };

        var nested = FormPayloadBuilder.BuildNested(flat);

        var name = nested["name"].Should().BeOfType<Dictionary<string, object>>().Subject;
        name.Should().HaveCount(2);
        name["givenName"].Should().Be("A");
        name["familyName"].Should().Be("B");
    }
}
