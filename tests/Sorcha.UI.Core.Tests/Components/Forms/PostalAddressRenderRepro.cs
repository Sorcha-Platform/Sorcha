// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// Repro for the live AIAS defect (2026-07-28): on the "Your address" page the
/// <c>postcode</c> and <c>country</c> fields render as bare labels with no input, while
/// line1/line2/town/region render correctly. Schema is the real core primitive
/// <c>blueprints/schemas/sorcha-core/PostalAddress.v1.json</c>.
/// </summary>
public class PostalAddressRenderRepro : BunitContext
{
    private readonly ITestOutputHelper _out;

    public PostalAddressRenderRepro(ITestOutputHelper output)
    {
        _out = output;
        Services.AddMudServices();
        Services.AddSingleton<IFormSchemaService>(new FormSchemaService());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private const string PostalAddressSchema = """
        {
          "type": "object",
          "title": "Postal Address",
          "x-sections": [
            { "title": "Street",   "layout": "vertical",   "fields": ["line1", "line2"] },
            { "title": "Locality", "layout": "horizontal", "fields": ["town", "region", "postcode"] },
            { "title": "Country",  "layout": "vertical",   "fields": ["country"] }
          ],
          "properties": {
            "line1":    { "type": "string", "title": "Address line 1", "minLength": 1, "x-width": "full" },
            "line2":    { "type": "string", "title": "Address line 2", "x-width": "full" },
            "town":     { "type": "string", "title": "Town or City", "minLength": 1, "x-width": "third" },
            "region":   { "type": "string", "title": "Region", "x-width": "third" },
            "postcode": { "type": "string", "title": "Postcode", "minLength": 1, "x-address-lookup": true, "x-width": "third" },
            "country":  { "type": "string", "title": "Country", "minLength": 2, "x-width": "half" }
          },
          "required": ["line1", "town", "postcode", "country"]
        }
        """;

    [Fact]
    public void WhichControlDoesEachAddressFieldInferTo()
    {
        var svc = new FormSchemaService();
        var doc = JsonDocument.Parse(PostalAddressSchema);

        var root = svc.AutoGenerateForm([doc]);

        var flat = new List<Control>();
        void Walk(Control c, int depth)
        {
            _out.WriteLine($"{new string(' ', depth * 2)}{c.ControlType,-14} scope='{c.Scope}' title='{c.Title}'");
            flat.Add(c);
            foreach (var e in c.Elements) Walk(e, depth + 1);
        }
        Walk(root, 0);

        foreach (var expected in new[] { "line1", "line2", "town", "region", "postcode", "country" })
        {
            flat.Should().Contain(c => c.Scope.EndsWith(expected, StringComparison.Ordinal),
                $"'{expected}' must produce a control");
        }
    }
}
