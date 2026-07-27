# AIAS Cyber Questionnaire (M2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Morag presents her Assured Identity VC to start an 8-question cyber-hygiene questionnaire; an autonomous agent scores her answers into a band and issues an AIAS Cyber Level VC carrying that level plus the portrait mapped forward from the presentation.

**Architecture:** Four workstreams. Three are platform capabilities (a `Slider` form control, `OptionalClaims` on `CredentialRequirement`, numeric external-check facts in `Sorcha.Agent`); the fourth is demo assets in `demos/AIAS/` published to a new, separate Cyber register. Scoring lives in a declarative answer→points table read by a new `scored-questionnaire` check, so the spread can be retuned with a one-number edit.

**Tech Stack:** .NET 10 / C# 14, Blazor WASM + MudBlazor, xUnit v3 + FluentAssertions, JsonLogic 6.1.0, PowerShell 7 (demo provisioning).

**Design doc:** `docs/superpowers/specs/2026-07-26-aias-cyber-questionnaire-design.md`

## Global Constraints

- Every new `.cs` / `.razor` file starts with the two-line licence header: `// SPDX-License-Identifier: MIT` then `// Copyright (c) 2026 Sorcha Contributors` (`@* ... *@` form in `.razor`).
- Never hard-code `<Version>` in a `.csproj` — versioning is derived from the root `Directory.Build.props`.
- `Sorcha.UI.Components.User` has `RootNamespace` `Sorcha.UI.Core`. Files live under `Components.User/...` folders but namespaces are rooted at `Sorcha.UI.Core`.
- Renderer tests for that project live in `tests/Sorcha.UI.Core.Tests/Components/Forms/`, not in a `Components.User.Tests` folder.
- `dotnet test` takes ONE project path at a time. Run `dotnet build` before tests — stale DLLs produce phantom failures.
- Test naming: `MethodName_Scenario_ExpectedBehavior`.
- Blueprint level bands (locked): 24 = Platinum, 21–23 = Gold, 16–20 = Silver, 12–15 = Bronze, < 12 = Fail (no credential).
- Register names are capped at **38 characters** by `RegisterCreationOrchestrator.ValidateControlRecord`. The new register is named exactly `Acme Cyber Assurance` (20 chars).
- Branch per task group, PR to `master`. Never commit directly to `master`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Common/Sorcha.Blueprint.Models/Control.cs` | `ControlTypes.Slider` enum member (appended last) |
| `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialRequirement.cs` | `OptionalClaims` property |
| `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Forms/FormSchemaService.cs` | `x-slider` → `ControlTypes.Slider` inference |
| `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/Controls/SliderRenderer.razor` | The slider control |
| `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/ControlDispatcher.razor` | Dispatch case |
| `src/Services/Sorcha.Blueprint.Service/Services/Implementation/RequirementDcqlMapper.cs` | Thread optional claims into the DCQL ask |
| `src/Apps/Sorcha.Agent/Decision/Checks/IExternalCheck.cs` | `ExternalCheckResult.Numeric` |
| `src/Apps/Sorcha.Agent/Decision/Checks/ExternalCheckRunner.cs` | Numeric fact merge |
| `src/Apps/Sorcha.Agent/Decision/Checks/ChecksConfig.cs` | `Answers` / `Ranges` definitions |
| `src/Apps/Sorcha.Agent/Decision/Checks/ScoredQuestionnaireCheck.cs` | The scorer |
| `src/Apps/Sorcha.Agent/Decision/Checks/ExternalCheckFactory.cs` | `scored-questionnaire` build arm |
| `demos/AIAS/blueprints/aias-cyber-level.template.json` | The questionnaire workflow |
| `demos/AIAS/agent/cyber.{config,rules,checks}.json` | Cyber-mode agent |
| `demos/AIAS/AiasDemo.psm1` | Cyber register + per-template register targeting |
| `demos/AIAS/rehearse.ps1` | Three cyber assertion paths |

---

## Task 1: `Slider` control type and schema inference

**Files:**
- Modify: `src/Common/Sorcha.Blueprint.Models/Control.cs` (end of `ControlTypes` enum, after `DeviceKey`)
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Forms/FormSchemaService.cs:400-441`
- Test: `tests/Sorcha.UI.Core.Tests/Components/Forms/FormSchemaServiceTests.cs`

**Interfaces:**
- Produces: `ControlTypes.Slider` enum member; `FormSchemaService.AutoGenerateForm` returns a `Control` with `ControlType == ControlTypes.Slider` for any integer/number property carrying an `x-slider` object.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Sorcha.UI.Core.Tests/Components/Forms/FormSchemaServiceTests.cs`, inside the existing `FormSchemaServiceTests` class:

```csharp
    [Fact]
    public void AutoGenerateForm_IntegerWithXSlider_InfersSliderControl()
    {
        var schema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "sharedPasswordCount":{"type":"integer","minimum":0,"maximum":10,
                "x-slider":{"step":1,"minLabel":"None","maxLabel":"10 or more"}}}}
            """);

        var root = _sut.AutoGenerateForm([schema]);

        root.Elements.Should().ContainSingle()
            .Which.ControlType.Should().Be(ControlTypes.Slider);
    }

    [Fact]
    public void AutoGenerateForm_IntegerWithoutXSlider_StillInfersNumeric()
    {
        var schema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "deviceCount":{"type":"integer","minimum":0,"maximum":10}}}
            """);

        var root = _sut.AutoGenerateForm([schema]);

        root.Elements.Should().ContainSingle()
            .Which.ControlType.Should().Be(ControlTypes.Numeric);
    }

    [Fact]
    public void AutoGenerateForm_StringWithXSlider_DoesNotInferSlider()
    {
        var schema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "notANumber":{"type":"string","x-slider":{"step":1}}}}
            """);

        var root = _sut.AutoGenerateForm([schema]);

        root.Elements.Should().ContainSingle()
            .Which.ControlType.Should().Be(ControlTypes.TextLine);
    }
```

Add `using Sorcha.Blueprint.Models;` to the file's using block if not already present.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet build src/Common/Sorcha.Blueprint.Models/Sorcha.Blueprint.Models.csproj
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
```

Expected: compile error — `'ControlTypes' does not contain a definition for 'Slider'`.

- [ ] **Step 3: Add the enum member**

In `src/Common/Sorcha.Blueprint.Models/Control.cs`, change the tail of the `ControlTypes` enum from:

```csharp
    [DataAnnotations.Display(Name = "Device Key")]
    DeviceKey
}
```

to:

```csharp
    [DataAnnotations.Display(Name = "Device Key")]
    DeviceKey,

    /// <summary>
    /// Slider input for a bounded integer. Dispatched when a numeric field carries an
    /// <c>x-slider</c> object. Opt-in by design — inferring from <c>type: integer</c> plus
    /// <c>minimum</c>/<c>maximum</c> alone would silently convert every existing numeric field
    /// in every blueprint into a slider. Range comes from the standard <c>minimum</c> /
    /// <c>maximum</c> keywords so the validator enforces it server-side; <c>x-slider</c>
    /// carries only <c>step</c> and the optional end labels. Feature AIAS M2.
    /// </summary>
    [DataAnnotations.Display(Name = "Slider")]
    Slider
}
```

Appended last so no existing enum ordinal shifts.

- [ ] **Step 4: Add the inference branch**

In `FormSchemaService.InferControlFromSchema`, after the `hasAddressLookup` declaration (around line 407), add:

```csharp
        // A numeric field carrying `x-slider` renders as a slider rather than a spin box.
        // Opt-in by design (see ControlTypes.Slider): inferring from `type: integer` plus
        // minimum/maximum alone would silently convert every existing numeric field in every
        // blueprint into a slider.
        var hasSlider =
            schema.TryGetProperty("x-slider", out var sliderEl) &&
            sliderEl.ValueKind == JsonValueKind.Object;
```

Then, in the `controlType` if-chain, insert a branch immediately **before** the final `else` (the `type switch` block):

```csharp
        else if (hasSlider && type is "integer" or "number")
        {
            controlType = ControlTypes.Slider;
        }
```

The chain must end up in this order: `hasEnum` → `hasAddressLookup` → date formats → file formats → **hasSlider** → `type switch`.

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Sorcha.UI.Components.User.csproj
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
```

Expected: PASS, including the two regression tests (integer without `x-slider` still `Numeric`, string with `x-slider` still `TextLine`).

- [ ] **Step 6: Commit**

```bash
git checkout -b feature/aias-m2-slider-control
git add src/Common/Sorcha.Blueprint.Models/Control.cs \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Forms/FormSchemaService.cs \
        tests/Sorcha.UI.Core.Tests/Components/Forms/FormSchemaServiceTests.cs
git commit -m "feat: [AIAS M2] - x-slider schema extension infers a Slider control"
```

---

## Task 2: `SliderRenderer` component

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/Controls/SliderRenderer.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/ControlDispatcher.razor:41-43`
- Test: `tests/Sorcha.UI.Core.Tests/Components/Forms/Controls/SliderRendererTests.cs`

**Interfaces:**
- Consumes: `ControlTypes.Slider` from Task 1.
- Produces: `SliderRenderer` with a public static helper `SliderRenderer.ResolveInitialValue(int? current, int min)` returning `current ?? min`. The renderer writes an **`int`** into `FormContext` at `Control.Scope`.

- [ ] **Step 1: Write the failing test**

Create `tests/Sorcha.UI.Core.Tests/Components/Forms/Controls/SliderRendererTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Components.Forms.Controls;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms.Controls;

/// <summary>
/// Tests for <see cref="SliderRenderer.ResolveInitialValue"/> — the seeding rule that keeps an
/// absent field from defaulting to 0 when 0 is outside the declared range. Feature AIAS M2.
/// </summary>
public class SliderRendererTests
{
    [Fact]
    public void ResolveInitialValue_ValueAbsent_SeedsFromMinimum()
    {
        SliderRenderer.ResolveInitialValue(null, 3).Should().Be(3);
    }

    [Fact]
    public void ResolveInitialValue_ValuePresent_KeepsValue()
    {
        SliderRenderer.ResolveInitialValue(7, 0).Should().Be(7);
    }

    [Fact]
    public void ResolveInitialValue_ValuePresentAndZero_KeepsZeroRatherThanReseeding()
    {
        SliderRenderer.ResolveInitialValue(0, 3).Should().Be(0);
    }
}
```

Then create the render test — the integer-not-string guarantee is the one that silently breaks
scoring, so it needs a real render. Create
`tests/Sorcha.UI.Core.Tests/Components/Forms/Controls/SliderRendererRenderTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Components.Forms.Controls;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms.Controls;

/// <summary>
/// The slider must seed an absent field and write an INTEGER. A stringly-typed value would pass
/// schema validation but silently score 0 in every band comparison. Feature AIAS M2.
/// </summary>
public class SliderRendererRenderTests : BunitContext
{
    private const string SchemaJson = """
        {"type":"object","properties":{
          "sharedPasswordCount":{"type":"integer","minimum":2,"maximum":10,
            "x-slider":{"step":1,"minLabel":"None","maxLabel":"10 or more"}}}}
        """;

    public SliderRendererRenderTests()
    {
        Services.AddMudServices();
        Services.AddSingleton<IFormSchemaService>(new FormSchemaService());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static FormContext MakeContext() => new()
    {
        AllFieldsDisclosed = true,
        DataSchema = JsonDocument.Parse(SchemaJson)
    };

    private static Control MakeControl() => new()
    {
        ControlType = ControlTypes.Slider,
        Scope = "/sharedPasswordCount",
        Title = "How many of your accounts share a password?"
    };

    private void RenderSlider(FormContext ctx)
        => Render(builder =>
        {
            builder.OpenComponent<CascadingValue<FormContext>>(0);
            builder.AddAttribute(1, "Value", ctx);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenComponent<SliderRenderer>(0);
                inner.AddAttribute(1, "Control", MakeControl());
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        });

    [Fact]
    public void OnInit_AbsentValue_SeedsFromMinimumAndWritesAnInteger()
    {
        var ctx = MakeContext();

        RenderSlider(ctx);

        ctx.GetValue<int?>("/sharedPasswordCount").Should().Be(2);
    }

    [Fact]
    public void OnInit_AbsentValue_DoesNotWriteAString()
    {
        var ctx = MakeContext();

        RenderSlider(ctx);

        // GetValue<string> on an integer-backed node must not round-trip a value — if this
        // returns "2" the renderer wrote a string and every band comparison silently scores 0.
        ctx.GetValue<int?>("/sharedPasswordCount").Should().NotBeNull();
    }

    [Fact]
    public void OnInit_ExistingValue_IsPreserved()
    {
        var ctx = MakeContext();
        ctx.SetValue("/sharedPasswordCount", 7);

        RenderSlider(ctx);

        ctx.GetValue<int?>("/sharedPasswordCount").Should().Be(7);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
```

Expected: compile error — `The type or namespace name 'SliderRenderer' could not be found`.

- [ ] **Step 3: Create the renderer**

Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/Controls/SliderRenderer.razor`:

```razor
@* SPDX-License-Identifier: MIT *@
@* Copyright (c) 2026 Sorcha Contributors *@

@using Sorcha.Blueprint.Models
@using Sorcha.UI.Core.Models.Forms
@using Sorcha.UI.Core.Services.Forms

@inject IFormSchemaService SchemaService
@implements IDisposable

<div class="sorcha-slider" @onfocusin="OnFocusIn" @onfocusout="OnFocusOut">
    <MudText Typo="Typo.body1" Class="mb-1">
        @Control.Title
        @if (_isRequired)
        {
            <span aria-hidden="true"> *</span>
        }
    </MudText>

    <MudSlider T="int"
               Value="@_value"
               ValueChanged="OnValueChanged"
               Min="@_min"
               Max="@_max"
               Step="@_step"
               Disabled="@(IsDisabled || FormContext?.IsReadOnly == true)"
               Variant="Variant.Filled"
               ValueLabel="true"
               aria-label="@Control.Title" />

    <div class="d-flex justify-space-between">
        <MudText Typo="Typo.caption">@_minLabel</MudText>
        <MudText Typo="Typo.caption"><strong>@_value</strong></MudText>
        <MudText Typo="Typo.caption">@_maxLabel</MudText>
    </div>

    @if (!string.IsNullOrEmpty(_errorText))
    {
        <MudText Typo="Typo.caption" Color="Color.Error">@_errorText</MudText>
    }
</div>

@code {
    [CascadingParameter] public FormContext? FormContext { get; set; }
    [Parameter, EditorRequired] public Control Control { get; set; } = new();
    [Parameter] public bool IsDisabled { get; set; }

    private int _value;
    private int _min;
    private int _max = 10;
    private int _step = 1;
    private string _minLabel = string.Empty;
    private string _maxLabel = string.Empty;
    private string _errorText = string.Empty;
    private bool _isRequired;

    /// <summary>
    /// Seeding rule: an absent value starts at <paramref name="min"/> rather than 0, because 0
    /// may be outside the declared range. A present 0 is kept — it is a real answer.
    /// </summary>
    public static int ResolveInitialValue(int? current, int min) => current ?? min;

    protected override void OnInitialized()
    {
        if (FormContext is not null)
            FormContext.OnValidationChanged += HandleValidationChanged;
    }

    protected override void OnParametersSet()
    {
        _isRequired = SchemaService.IsRequired(FormContext?.DataSchema, Control.Scope);

        var fieldSchema = SchemaService.GetSchemaForScope(FormContext?.DataSchema, Control.Scope);
        if (fieldSchema.HasValue)
        {
            if (fieldSchema.Value.TryGetProperty("minimum", out var minEl))
                _min = minEl.GetInt32();
            if (fieldSchema.Value.TryGetProperty("maximum", out var maxEl))
                _max = maxEl.GetInt32();

            if (fieldSchema.Value.TryGetProperty("x-slider", out var slider)
                && slider.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (slider.TryGetProperty("step", out var stepEl) && stepEl.TryGetInt32(out var step) && step > 0)
                    _step = step;
                if (slider.TryGetProperty("minLabel", out var minLabelEl))
                    _minLabel = minLabelEl.GetString() ?? string.Empty;
                if (slider.TryGetProperty("maxLabel", out var maxLabelEl))
                    _maxLabel = maxLabelEl.GetString() ?? string.Empty;
            }
        }

        _value = ResolveInitialValue(FormContext?.GetValue<int?>(Control.Scope), _min);

        // Write the seeded value straight back so an untouched slider still submits a real
        // integer. Without this, a citizen who accepts the default answer submits nothing and
        // the scoring check sees a missing field.
        FormContext?.SetValue(Control.Scope, _value);

        UpdateErrors();
    }

    private void OnValueChanged(int newValue)
    {
        _value = newValue;
        FormContext?.SetValue(Control.Scope, newValue);

        if (FormContext is not null)
        {
            var errors = SchemaService.ValidateField(FormContext.DataSchema, Control.Scope, newValue);
            FormContext.SetErrors(Control.Scope, errors);
        }

        UpdateErrors();
    }

    private void HandleValidationChanged()
    {
        UpdateErrors();
        InvokeAsync(StateHasChanged);
    }

    private void UpdateErrors()
    {
        var errors = FormContext?.GetErrors(Control.Scope) ?? [];
        _errorText = errors.Count > 0 ? string.Join("; ", errors) : string.Empty;
    }

    private void OnFocusIn() => FormContext?.SetFocusedField(Control.Scope);

    private void OnFocusOut()
    {
        if (FormContext?.FocusedFieldScope == Control.Scope)
            FormContext.SetFocusedField(null);
    }

    public void Dispose()
    {
        if (FormContext is not null)
            FormContext.OnValidationChanged -= HandleValidationChanged;
    }
}
```

- [ ] **Step 4: Add the dispatcher case**

In `ControlDispatcher.razor`, immediately after the `ControlTypes.Numeric` case (lines 41–43), insert:

```razor
        case ControlTypes.Slider:
            <SliderRenderer Control="Control" IsDisabled="_isDisabled" />
            break;

```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Sorcha.UI.Components.User.csproj
dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj
```

Expected: PASS.

- [ ] **Step 6: Document the extension**

In `.claude/skills/blueprint-builder/SKILL.md`, add a section documenting `x-slider`:

```markdown
### `x-slider` — bounded integer slider

A numeric property carrying an `x-slider` object renders as a slider instead of a spin box.

​```jsonc
"sharedPasswordCount": {
  "type": "integer",
  "title": "How many of your accounts share a password?",
  "minimum": 0,
  "maximum": 10,
  "x-slider": { "step": 1, "minLabel": "None", "maxLabel": "10 or more" }
}
​```

Range comes from the standard `minimum` / `maximum` keywords, NOT from inside `x-slider` — they
are real JSON Schema keywords, so the validator enforces the range server-side and a hand-crafted
submission cannot post an out-of-range value. `x-slider` carries only `step` and the optional
`minLabel` / `maxLabel` end captions.

Dispatch is opt-in: an integer field WITHOUT `x-slider` still renders as a numeric input.
An untouched slider submits its seeded value (the `minimum`), so the field is never absent.
```

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/Controls/SliderRenderer.razor \
        src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/ControlDispatcher.razor \
        tests/Sorcha.UI.Core.Tests/Components/Forms/Controls/SliderRendererTests.cs \
        .claude/skills/blueprint-builder/SKILL.md
git commit -m "feat: [AIAS M2] - SliderRenderer for x-slider integer fields"
```

---

## Task 3: `OptionalClaims` on `CredentialRequirement`

**Files:**
- Modify: `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialRequirement.cs:46` (after `RequiredClaims`)
- Modify: `src/Services/Sorcha.Blueprint.Service/Services/Implementation/RequirementDcqlMapper.cs:54-55`
- Test: `tests/Sorcha.Blueprint.Service.Tests/Services/RequirementDcqlMapperTests.cs`

**Interfaces:**
- Produces: `CredentialRequirement.OptionalClaims` (`IEnumerable<ClaimConstraint>?`, JSON name `optionalClaims`), flowing into the existing `DcqlCredentialAsk.SdJwt(id, vct, requiredClaims, optionalClaims)` optional parameter.

**Why this exists:** put `portrait` in `requiredClaims` and a portrait-less Assured Identity fails the OID4VP gate with a generic protocol error, so the agent never sees the presentation and the on-brand AIAS rejection never fires. Requesting it as *optional* is what lets a portrait-less credential satisfy the gate and reach the agent.

- [ ] **Step 1: Write the failing test**

Create or append to `tests/Sorcha.Blueprint.Service.Tests/Services/RequirementDcqlMapperTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Services.Implementation;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

public class RequirementDcqlMapperOptionalClaimsTests
{
    [Fact]
    public void Map_RequirementWithOptionalClaims_IncludesThemInTheAsk()
    {
        var requirement = new CredentialRequirement
        {
            Type = "https://sorcha.dev/vc/assured-identity/v1",
            RequiredClaims = [new ClaimConstraint { ClaimName = "givenName" }],
            OptionalClaims = [new ClaimConstraint { ClaimName = "portrait" }]
        };

        var query = RequirementDcqlMapper.Build([requirement]);

        var json = System.Text.Json.JsonSerializer.Serialize(query);
        json.Should().Contain("portrait");
        json.Should().Contain("givenName");
    }

    [Fact]
    public void Build_RequirementWithoutOptionalClaims_BehavesAsBefore()
    {
        var requirement = new CredentialRequirement
        {
            Type = "https://sorcha.dev/vc/assured-identity/v1",
            RequiredClaims = [new ClaimConstraint { ClaimName = "givenName" }]
        };

        var query = RequirementDcqlMapper.Build([requirement]);

        System.Text.Json.JsonSerializer.Serialize(query).Should().Contain("givenName");
    }
}
```

The public entry point is `RequirementDcqlMapper.Build(IReadOnlyList<CredentialRequirementModel> requirements)` — a static method that throws `ArgumentException` on an empty list. `CredentialRequirementModel` is the file's alias for `CredentialRequirement`.

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet build src/Common/Sorcha.Blueprint.Models/Sorcha.Blueprint.Models.csproj
dotnet test tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj
```

Expected: compile error — `'CredentialRequirement' does not contain a definition for 'OptionalClaims'`.

- [ ] **Step 3: Add the model property**

In `CredentialRequirement.cs`, immediately after the `RequiredClaims` property, add:

```csharp
    /// <summary>
    /// Claims the verifier asks for but the holder may withhold. A credential that omits every
    /// optional claim still satisfies the requirement — which is what lets a workflow accept the
    /// presentation and then make its own decision about a missing claim, rather than failing at
    /// the OID4VP gate with a generic protocol error.
    /// </summary>
    [JsonPropertyName("optionalClaims")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<ClaimConstraint>? OptionalClaims { get; set; }
```

- [ ] **Step 4: Thread it through the mapper**

In `RequirementDcqlMapper.cs`, replace lines 54–55:

```csharp
            var requiredClaims = req.RequiredClaims?.Select(c => c.ClaimName).ToList() ?? [];
            asks.Add(DcqlCredentialAsk.SdJwt(id, req.Type, requiredClaims));
```

with:

```csharp
            var requiredClaims = req.RequiredClaims?.Select(c => c.ClaimName).ToList() ?? [];
            var optionalClaims = req.OptionalClaims?.Select(c => c.ClaimName).ToList();
            asks.Add(DcqlCredentialAsk.SdJwt(id, req.Type, requiredClaims, optionalClaims));
```

`DcqlCredentialAsk.SdJwt` already accepts `optionalClaims` as a trailing optional parameter and `DcqlRequestBuilder` already consumes `ask.OptionalClaims`, so no change is needed below this line.

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet build src/Services/Sorcha.Blueprint.Service/Sorcha.Blueprint.Service.csproj
dotnet test tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git checkout -b feature/aias-m2-optional-claims
git add src/Common/Sorcha.Blueprint.Models/Credentials/CredentialRequirement.cs \
        src/Services/Sorcha.Blueprint.Service/Services/Implementation/RequirementDcqlMapper.cs \
        tests/Sorcha.Blueprint.Service.Tests/Services/RequirementDcqlMapperTests.cs
git commit -m "feat: [AIAS M2] - optionalClaims on CredentialRequirement reach the DCQL ask"
```

---

## Task 4: Numeric external-check facts

**Files:**
- Modify: `src/Apps/Sorcha.Agent/Decision/Checks/IExternalCheck.cs` (the `ExternalCheckResult` record)
- Modify: `src/Apps/Sorcha.Agent/Decision/Checks/ExternalCheckRunner.cs:38-43`
- Test: `tests/Sorcha.Agent.Tests/Decision/Checks/ExternalCheckRunnerTests.cs`

**Interfaces:**
- Produces: `ExternalCheckResult(string Name, bool Value, string? Detail = null, double? Numeric = null)`. When `Numeric` is set, `ExternalCheckRunner` merges the fact as a JSON **number**; otherwise as a boolean. The new parameter is optional and last, so the four existing checks compile unchanged.

- [ ] **Step 1: Write the failing test**

Append to `tests/Sorcha.Agent.Tests/Decision/Checks/ExternalCheckRunnerTests.cs`:

```csharp
    private sealed class NumericStubCheck(string name, double value) : IExternalCheck
    {
        public string Name { get; } = name;

        public Task<ExternalCheckResult> EvaluateAsync(
            IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
            => Task.FromResult(new ExternalCheckResult(Name, true, null, value));
    }

    [Fact]
    public async Task RunAsync_CheckReturnsNumeric_MergesFactAsNumber()
    {
        var runner = new ExternalCheckRunner([new NumericStubCheck("cyberScore", 18)]);

        var facts = await runner.RunAsync(CheckTestSupport.Payload("{}"), default);

        facts["cyberScore"].Should().Be(18d);
    }

    [Fact]
    public async Task RunAsync_CheckReturnsBooleanOnly_StillMergesAsBoolean()
    {
        var runner = new ExternalCheckRunner([new FieldPresentCheck("photoPresent", "/portrait")]);

        var facts = await runner.RunAsync(
            CheckTestSupport.Payload("""{ "portrait": "abc" }"""), default);

        facts["photoPresent"].Should().Be(true);
    }
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/Sorcha.Agent.Tests/Sorcha.Agent.Tests.csproj
```

Expected: compile error — the `ExternalCheckResult` constructor takes at most 3 arguments.

- [ ] **Step 3: Widen the result record**

In `IExternalCheck.cs`, replace:

```csharp
public sealed record ExternalCheckResult(string Name, bool Value, string? Detail = null);
```

with:

```csharp
/// <param name="Numeric">
/// Optional numeric result. When set, <see cref="ExternalCheckRunner"/> merges the fact at
/// <c>/checks/{Name}</c> as a JSON number instead of a boolean, so JSON-Logic rules can compare
/// it (<c>{"&lt;": [{"var": "checks.cyberScore"}, 12]}</c>). Optional and last so every existing
/// check compiles unchanged.
/// </param>
public sealed record ExternalCheckResult(
    string Name, bool Value, string? Detail = null, double? Numeric = null);
```

Update the record's existing `<param name="Value">` doc line to read: `Boolean result merged at <c>/checks/{Name}</c> when <paramref name="Numeric"/> is null.`

- [ ] **Step 4: Merge numerically in the runner**

In `ExternalCheckRunner.RunAsync`, replace:

```csharp
            merged[result.Name] = result.Value;
```

with:

```csharp
            // A numeric check merges its number; everything else merges its boolean. A check that
            // faults is contained by SafeEvaluateAsync into a boolean false, which JSON Logic
            // coerces to 0 — so a broken scorer lands in the lowest band rather than passing.
            merged[result.Name] = result.Numeric.HasValue
                ? result.Numeric.Value
                : result.Value;
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet build src/Apps/Sorcha.Agent/Sorcha.Agent.csproj
dotnet test tests/Sorcha.Agent.Tests/Sorcha.Agent.Tests.csproj
```

Expected: PASS, with all pre-existing check tests still green.

- [ ] **Step 6: Commit**

```bash
git checkout -b feature/aias-m2-agent-scoring
git add src/Apps/Sorcha.Agent/Decision/Checks/IExternalCheck.cs \
        src/Apps/Sorcha.Agent/Decision/Checks/ExternalCheckRunner.cs \
        tests/Sorcha.Agent.Tests/Decision/Checks/ExternalCheckRunnerTests.cs
git commit -m "feat: [AIAS M2] - external checks can produce numeric facts"
```

---

## Task 5: `ScoredQuestionnaireCheck`

**Files:**
- Create: `src/Apps/Sorcha.Agent/Decision/Checks/ScoredQuestionnaireCheck.cs`
- Modify: `src/Apps/Sorcha.Agent/Decision/Checks/ChecksConfig.cs` (add `Answers`, `Ranges` to `CheckDefinition`)
- Modify: `src/Apps/Sorcha.Agent/Decision/Checks/ExternalCheckFactory.cs:50-64` (new build arm)
- Test: `tests/Sorcha.Agent.Tests/Decision/Checks/ScoredQuestionnaireCheckTests.cs`

**Interfaces:**
- Consumes: `ExternalCheckResult.Numeric` from Task 4; `PayloadPointer.Resolve` / `ResolveString`.
- Produces: `ScoredQuestionnaireCheck(string name, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> answers, IReadOnlyDictionary<string, IReadOnlyList<ScoreRange>> ranges)`, and `ScoreRange(int? Max, int Points)`. Emits the total under `checks.{name}` as a number, plus `checks.{name}Detail` describing the breakdown.

**Range semantics:** `ranges` entries are evaluated **top-down** and `Max` is an **inclusive** upper bound. The first entry whose `Max` is `>=` the submitted value wins; an entry with `Max == null` is the catch-all.

- [ ] **Step 1: Write the failing tests**

Create `tests/Sorcha.Agent.Tests/Decision/Checks/ScoredQuestionnaireCheckTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Decision.Checks;

namespace Sorcha.Agent.Tests.Decision.Checks;

public class ScoredQuestionnaireCheckTests
{
    private static ScoredQuestionnaireCheck Build() => new(
        "cyberScore",
        new Dictionary<string, IReadOnlyDictionary<string, int>>
        {
            ["/passwordStorage"] = new Dictionary<string, int>
            {
                ["A password manager"] = 3,
                ["Saved in my browser"] = 2,
                ["A notebook by the desk"] = 1,
                ["The same one everywhere, and hope"] = 0
            }
        },
        new Dictionary<string, IReadOnlyList<ScoreRange>>
        {
            ["/sharedPasswordCount"] =
            [
                new ScoreRange(0, 3), new ScoreRange(2, 2), new ScoreRange(5, 1), new ScoreRange(null, 0)
            ]
        });

    [Fact]
    public async Task EvaluateAsync_TopAnswers_ScoresMaximum()
    {
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload("""
            { "passwordStorage": "A password manager", "sharedPasswordCount": 0 }
            """), default);

        result.Numeric.Should().Be(6);
    }

    [Fact]
    public async Task EvaluateAsync_UnrecognisedAnswer_ScoresZeroForThatQuestion()
    {
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload("""
            { "passwordStorage": "Tattooed on my arm", "sharedPasswordCount": 0 }
            """), default);

        result.Numeric.Should().Be(3);
    }

    [Fact]
    public async Task EvaluateAsync_MissingField_ScoresZeroForThatQuestion()
    {
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload("""
            { "sharedPasswordCount": 0 }
            """), default);

        result.Numeric.Should().Be(3);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 0)]
    [InlineData(99, 0)]
    public async Task EvaluateAsync_RangeBoundaries_ScoreInclusively(int submitted, int expected)
    {
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload($$"""
            { "sharedPasswordCount": {{submitted}} }
            """), default);

        result.Numeric.Should().Be(expected);
    }

    [Fact]
    public async Task EvaluateAsync_Always_ReportsBreakdownInDetail()
    {
        var result = await Build().EvaluateAsync(CheckTestSupport.Payload("""
            { "passwordStorage": "Saved in my browser", "sharedPasswordCount": 4 }
            """), default);

        result.Numeric.Should().Be(3);
        result.Detail.Should().Contain("/passwordStorage=2");
        result.Detail.Should().Contain("/sharedPasswordCount=1");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/Sorcha.Agent.Tests/Sorcha.Agent.Tests.csproj
```

Expected: compile error — `ScoredQuestionnaireCheck` and `ScoreRange` not found.

- [ ] **Step 3: Write the check**

Create `src/Apps/Sorcha.Agent/Decision/Checks/ScoredQuestionnaireCheck.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json.Nodes;

namespace Sorcha.Agent.Decision.Checks;

/// <summary>
/// One banded range for a numeric answer. <paramref name="Max"/> is an INCLUSIVE upper bound;
/// a null <paramref name="Max"/> is the catch-all and must be last.
/// </summary>
public sealed record ScoreRange(int? Max, int Points);

/// <summary>
/// Sums a questionnaire into a single numeric fact. Two scoring modes, because the two answer
/// shapes differ: <c>answers</c> maps an exact submitted string to points (graded multiple
/// choice), <c>ranges</c> maps a submitted number into a band (slider).
///
/// There is deliberately no "could not score" outcome. Every question is schema-<c>required</c>,
/// so the validator guarantees the answers are present before the agent sees the payload, and an
/// unrecognised or missing answer simply scores 0. A hard fault is contained by
/// <see cref="ExternalCheckRunner"/> into a boolean false, which JSON Logic coerces to 0 — so a
/// broken scorer lands in the lowest band and issues nothing.
/// </summary>
public sealed class ScoredQuestionnaireCheck : IExternalCheck
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> _answers;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ScoreRange>> _ranges;

    /// <summary>Creates the scorer.</summary>
    /// <param name="name">Fact key (e.g. <c>cyberScore</c>).</param>
    /// <param name="answers">JSON-Pointer → (exact answer string → points).</param>
    /// <param name="ranges">JSON-Pointer → ordered inclusive-upper-bound ranges.</param>
    public ScoredQuestionnaireCheck(
        string name,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> answers,
        IReadOnlyDictionary<string, IReadOnlyList<ScoreRange>> ranges)
    {
        Name = name;
        _answers = answers ?? throw new ArgumentNullException(nameof(answers));
        _ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Task<ExternalCheckResult> EvaluateAsync(
        IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
    {
        var total = 0;
        var breakdown = new List<string>();

        foreach (var (pointer, table) in _answers)
        {
            var answer = PayloadPointer.ResolveString(payload, pointer);
            var points = answer is not null && table.TryGetValue(answer, out var p) ? p : 0;
            total += points;
            breakdown.Add($"{pointer}={points}");
        }

        foreach (var (pointer, bands) in _ranges)
        {
            var points = ScoreRangeValue(PayloadPointer.Resolve(payload, pointer), bands);
            total += points;
            breakdown.Add($"{pointer}={points}");
        }

        var detail = $"score {total} ({string.Join(", ", breakdown)})";
        return Task.FromResult(new ExternalCheckResult(Name, true, detail, total));
    }

    private static int ScoreRangeValue(JsonNode? node, IReadOnlyList<ScoreRange> bands)
    {
        if (node is not JsonValue value || !TryReadInt(value, out var submitted))
        {
            // Absent or non-numeric: score the catch-all, or 0 when none is declared.
            return bands.FirstOrDefault(b => b.Max is null)?.Points ?? 0;
        }

        foreach (var band in bands)
        {
            if (band.Max is null || submitted <= band.Max.Value)
                return band.Points;
        }

        return 0;
    }

    private static bool TryReadInt(JsonValue value, out int result)
    {
        if (value.TryGetValue(out int i)) { result = i; return true; }
        if (value.TryGetValue(out double d)) { result = (int)Math.Round(d); return true; }
        result = 0;
        return false;
    }
}
```

- [ ] **Step 4: Extend the config shape**

In `ChecksConfig.cs`, update the `CheckDefinition.Type` doc comment to include `scored-questionnaire`, and append these two properties to `CheckDefinition`:

```csharp
    /// <summary>
    /// JSON-Pointer → (exact answer string → points), for <c>scored-questionnaire</c>. The answer
    /// keys must match the blueprint's <c>enum</c> values verbatim — the answer sentence IS the
    /// scoring key, because the Selection control has no separate display labels.
    /// </summary>
    public Dictionary<string, Dictionary<string, int>>? Answers { get; init; }

    /// <summary>
    /// JSON-Pointer → ordered bands, for <c>scored-questionnaire</c>. Evaluated top-down with
    /// <c>max</c> as an INCLUSIVE upper bound; an entry with no <c>max</c> is the catch-all.
    /// </summary>
    public Dictionary<string, RangeDefinition[]>? Ranges { get; init; }
```

And add this record at the end of the file:

```csharp
/// <summary>One band in a <c>scored-questionnaire</c> range: inclusive upper bound plus points.</summary>
public sealed record RangeDefinition
{
    /// <summary>Inclusive upper bound. Null makes this the catch-all (must be last).</summary>
    public int? Max { get; init; }

    /// <summary>Points awarded when this band matches.</summary>
    public int Points { get; init; }
}
```

- [ ] **Step 5: Wire the factory**

In `ExternalCheckFactory.Build`, add this arm to the `switch` immediately before the `_ =>` default:

```csharp
            "scored-questionnaire" => new ScoredQuestionnaireCheck(
                def.Name,
                (def.Answers ?? [])
                    .ToDictionary(
                        kv => kv.Key,
                        kv => (IReadOnlyDictionary<string, int>)kv.Value,
                        StringComparer.Ordinal),
                (def.Ranges ?? [])
                    .ToDictionary(
                        kv => kv.Key,
                        kv => (IReadOnlyList<ScoreRange>)kv.Value
                            .Select(r => new ScoreRange(r.Max, r.Points)).ToArray(),
                        StringComparer.Ordinal)),
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet build src/Apps/Sorcha.Agent/Sorcha.Agent.csproj
dotnet test tests/Sorcha.Agent.Tests/Sorcha.Agent.Tests.csproj
```

Expected: PASS — all 12 new assertions plus the existing suite.

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.Agent/Decision/Checks/ScoredQuestionnaireCheck.cs \
        src/Apps/Sorcha.Agent/Decision/Checks/ChecksConfig.cs \
        src/Apps/Sorcha.Agent/Decision/Checks/ExternalCheckFactory.cs \
        tests/Sorcha.Agent.Tests/Decision/Checks/ScoredQuestionnaireCheckTests.cs
git commit -m "feat: [AIAS M2] - scored-questionnaire check sums answers into a numeric fact"
```

---

## Task 6: Cyber questionnaire blueprint template

**Files:**
- Create: `demos/AIAS/blueprints/aias-cyber-level.template.json`

**Interfaces:**
- Consumes: `x-slider` (Task 1/2), `optionalClaims` (Task 3).
- Produces: the enum answer strings that Task 7's `cyber.checks.json` scores against. **These two files must agree verbatim.**

- [ ] **Step 1: Create the blueprint**

Create `demos/AIAS/blueprints/aias-cyber-level.template.json`:

```json
{
  "id": "aias-cyber-level-v1",
  "title": "AIAS Cyber Level",
  "description": "AIAS conference demo M2. The citizen presents their Assured Identity credential to prove entitlement, answers an eight-question cyber-hygiene questionnaire, and an autonomous agent scores the answers into a band and issues a Cyber Level credential carrying the level plus the portrait mapped forward from the verified presentation. The issuing-agency name is injected via the {{issuerName}} token at provision time.",
  "version": 1,
  "category": "identity",
  "tags": ["aias", "cyber", "questionnaire", "scoring", "verifiable-credential", "credential-presentation", "oid4vp"],
  "author": "Sorcha Team",
  "published": true,
  "template": {
    "id": "aias-cyber-level",
    "title": "AIAS Cyber Level",
    "description": "Present your Assured Identity, answer eight questions about your cyber habits, and receive a leveled Cyber Level credential.",
    "version": 1,
    "metadata": {
      "category": "Identity & Credentials",
      "complexity": "Simple",
      "actions": "2",
      "features": "OpenID4VP, Credential Presentation Gate, Optional Claims, Slider Inputs, Autonomous Scoring, Verifiable Credential, Portrait Carry-Forward",
      "sector": "Identity & Trust"
    },
    "participants": [
      {
        "id": "citizen",
        "name": "Credential Holder",
        "organisation": "Public",
        "description": "Holds an AIAS Assured Identity credential and is applying for a Cyber Level"
      },
      {
        "id": "aias-analyst",
        "name": "AIAS Cyber Analyst",
        "organisation": "{{issuerName}}",
        "description": "The autonomous Cyber-mode agent that scores the questionnaire and issues the leveled credential"
      }
    ],
    "actions": [
      {
        "id": 1,
        "title": "Your cyber health check",
        "description": "Present your Assured Identity credential to prove it is yours, then answer eight quick questions about how you look after your accounts and devices.",
        "sender": "citizen",
        "isStartingAction": true,
        "credentialRequirements": [
          {
            "type": "https://sorcha.dev/vc/assured-identity/v1",
            "presentationSource": "SorchaWallet",
            "requiredClaims": [
              { "claimName": "givenName" },
              { "claimName": "familyName" }
            ],
            "optionalClaims": [
              { "claimName": "portrait" }
            ]
          }
        ],
        "dataSchemas": [
          {
            "type": "object",
            "x-introduction": "Eight questions about your cyber habits. Answer honestly — AIAS is scoring your actual posture, not your good intentions.",
            "x-sections": [
              { "title": "Passwords", "fields": ["passwordStorage", "passwordChangeHabit", "sharedPasswordCount", "streamingPasswordSharers"] },
              { "title": "Accounts and devices", "fields": ["emailSecondStep", "phoneUpdates", "laptopLoss"] },
              { "title": "Spotting trouble", "fields": ["suspiciousEmail"] }
            ],
            "required": [
              "passwordStorage", "emailSecondStep", "passwordChangeHabit", "phoneUpdates",
              "suspiciousEmail", "laptopLoss", "sharedPasswordCount", "streamingPasswordSharers"
            ],
            "properties": {
              "passwordStorage": {
                "type": "string",
                "title": "How do you keep track of your passwords?",
                "enum": [
                  "A password manager",
                  "Saved in my browser",
                  "A notebook by the desk",
                  "The same one everywhere, and hope"
                ]
              },
              "emailSecondStep": {
                "type": "string",
                "title": "Is there a second step when you sign in to your email?",
                "enum": [
                  "Yes — an app or a hardware key",
                  "Yes — a code by text message",
                  "Only when it nags me",
                  "No, just the password"
                ]
              },
              "passwordChangeHabit": {
                "type": "string",
                "title": "How often do you change your passwords?",
                "enum": [
                  "Only when I think one's been exposed",
                  "Once a year, whether it needs it or not",
                  "Every 30 days, like clockwork",
                  "Change them?"
                ]
              },
              "phoneUpdates": {
                "type": "string",
                "title": "Your phone offers an update. What happens?",
                "enum": [
                  "It installs automatically",
                  "I install it within a few days",
                  "I click 'remind me later', repeatedly",
                  "There's a red badge I've been ignoring since spring"
                ]
              },
              "suspiciousEmail": {
                "type": "string",
                "title": "An email says your bank account is locked. What do you do?",
                "enum": [
                  "Ignore the email and open the bank's app myself",
                  "Check the sender address carefully, then decide",
                  "Hover the link to see where it goes, then decide",
                  "Click it — it looked legitimate"
                ]
              },
              "laptopLoss": {
                "type": "string",
                "title": "If your laptop vanished this afternoon, what would you lose?",
                "enum": [
                  "Nothing — it backs up automatically",
                  "A day's work at most",
                  "I copied things to a USB stick once",
                  "Everything. Please don't say that."
                ]
              },
              "sharedPasswordCount": {
                "type": "integer",
                "title": "How many of your accounts share a password?",
                "minimum": 0,
                "maximum": 10,
                "x-slider": { "step": 1, "minLabel": "None", "maxLabel": "10 or more" }
              },
              "streamingPasswordSharers": {
                "type": "integer",
                "title": "How many people know your streaming password?",
                "minimum": 0,
                "maximum": 10,
                "x-slider": { "step": 1, "minLabel": "Just me", "maxLabel": "10 or more" }
              }
            }
          }
        ],
        "disclosures": [
          { "participantAddress": "citizen", "dataPointers": ["/*"] },
          { "participantAddress": "aias-analyst", "dataPointers": ["/*"] }
        ],
        "routes": [
          {
            "id": "to-scoring",
            "nextActionIds": [2],
            "isDefault": true,
            "description": "Answers submitted — hand to the AIAS cyber analyst for scoring"
          }
        ]
      },
      {
        "id": 2,
        "title": "AIAS scores your answers",
        "description": "The AIAS cyber analyst scores the questionnaire, maps the total to a level band, and issues a Cyber Level credential carrying the level and the portrait from the presented Assured Identity.",
        "sender": "aias-analyst",
        "requiredPriorActions": [1],
        "dataSchemas": [
          {
            "type": "object",
            "x-introduction": "Automatic scoring — no human decision.",
            "properties": {
              "decision": {
                "type": "string",
                "title": "Decision",
                "enum": ["approved", "rejected"]
              },
              "level": {
                "type": "string",
                "title": "Cyber level",
                "enum": ["Bronze", "Silver", "Gold", "Platinum"]
              },
              "reasonCode": { "type": "string", "title": "Reason code" },
              "verificationNotes": { "type": "string", "title": "Analyst notes" }
            }
          }
        ],
        "credentialIssuanceConfig": {
          "credentialType": "CyberLevelCredential",
          "vct": "https://sorcha.dev/vc/cyber-level/v1",
          "displayName": "AIAS Cyber Level",
          "targetAudience": "SorchaLocalWallet",
          "recipientParticipantId": "citizen",
          "issuanceCondition": { "==": [{ "var": "decision" }, "approved"] },
          "claimMappings": [
            { "claimName": "level", "sourceField": "/level" },
            { "claimName": "portrait", "sourceField": "/presentedCredential/portrait" },
            { "claimName": "givenName", "sourceField": "/presentedCredential/givenName" },
            { "claimName": "familyName", "sourceField": "/presentedCredential/familyName" }
          ],
          "disclosable": ["level", "portrait", "givenName", "familyName"],
          "expiryDuration": "P1Y"
        },
        "disclosures": [
          { "participantAddress": "aias-analyst", "dataPointers": ["/*"] },
          { "participantAddress": "citizen", "dataPointers": ["/*"] }
        ],
        "routes": [
          {
            "id": "approved-terminal",
            "nextActionIds": [],
            "condition": { "==": [{ "var": "decision" }, "approved"] },
            "description": "Level awarded — credential issued"
          },
          {
            "id": "rejected-terminal",
            "nextActionIds": [],
            "isDefault": true,
            "description": "No level awarded",
            "x-decision-notice": {
              "reasonCodeField": "/reasonCode",
              "reasons": {
                "no-portrait": "AIAS cannot issue a Cyber Level without a face to put on it. Add a photo to your Assured Identity and come back.",
                "cyber-fail": "AIAS admires the honesty, but cannot certify this. Fix the shared passwords and try again."
              },
              "fallbackMessage": "AIAS could not award a Cyber Level on this occasion."
            }
          }
        ]
      }
    ]
  }
}
```

- [ ] **Step 2: Validate the JSON parses and the enum/required sets agree**

```bash
python -c "
import json
d=json.load(open('demos/AIAS/blueprints/aias-cyber-level.template.json'))
a1=d['template']['actions'][0]['dataSchemas'][0]
props=set(a1['properties'])
req=set(a1['required'])
assert props==req, f'required != properties: {props ^ req}'
enums={k:len(v['enum']) for k,v in a1['properties'].items() if 'enum' in v}
sliders=[k for k,v in a1['properties'].items() if 'x-slider' in v]
print('questions:', len(props), '| enum fields:', len(enums), '| sliders:', len(sliders))
assert len(enums)==6 and len(sliders)==2
print('OK')
"
```

Expected: `questions: 8 | enum fields: 6 | sliders: 2` then `OK`.

- [ ] **Step 3: Commit**

```bash
git checkout -b feature/aias-m2-demo-assets
git add demos/AIAS/blueprints/aias-cyber-level.template.json
git commit -m "feat: [AIAS M2] - cyber questionnaire blueprint template"
```

---

## Task 7: Cyber agent config trio and band-boundary tests

**Files:**
- Create: `demos/AIAS/agent/cyber.checks.json`
- Create: `demos/AIAS/agent/cyber.rules.json`
- Test: `tests/Sorcha.Agent.Tests/Decision/Checks/CyberBandBoundaryTests.cs`

**Interfaces:**
- Consumes: `ScoredQuestionnaireCheck` (Task 5), the enum strings from Task 6.
- Produces: the fact keys `checks.portraitPresent` (bool) and `checks.cyberScore` (number) that the rules decide on.

`cyber.config.json` is generated at provision time by `Build-AiasAgentConfig` (Task 8), not committed.

- [ ] **Step 1: Write the checks config**

Create `demos/AIAS/agent/cyber.checks.json`:

```json
{
  "$comment": "AIAS Cyber-mode external checks (M2). Answer keys MUST match the enum values in demos/AIAS/blueprints/aias-cyber-level.template.json verbatim — the Selection control has no separate display labels, so the answer sentence IS the scoring key. Range entries are evaluated top-down with 'max' as an INCLUSIVE upper bound; the entry with no 'max' is the catch-all.",
  "checks": [
    {
      "name": "portraitPresent",
      "type": "field-present",
      "field": "/presentedCredential/portrait"
    },
    {
      "name": "cyberScore",
      "type": "scored-questionnaire",
      "answers": {
        "/passwordStorage": {
          "A password manager": 3,
          "Saved in my browser": 2,
          "A notebook by the desk": 1,
          "The same one everywhere, and hope": 0
        },
        "/emailSecondStep": {
          "Yes — an app or a hardware key": 3,
          "Yes — a code by text message": 2,
          "Only when it nags me": 1,
          "No, just the password": 0
        },
        "/passwordChangeHabit": {
          "Only when I think one's been exposed": 3,
          "Once a year, whether it needs it or not": 2,
          "Every 30 days, like clockwork": 1,
          "Change them?": 0
        },
        "/phoneUpdates": {
          "It installs automatically": 3,
          "I install it within a few days": 2,
          "I click 'remind me later', repeatedly": 1,
          "There's a red badge I've been ignoring since spring": 0
        },
        "/suspiciousEmail": {
          "Ignore the email and open the bank's app myself": 3,
          "Check the sender address carefully, then decide": 1,
          "Hover the link to see where it goes, then decide": 1,
          "Click it — it looked legitimate": 0
        },
        "/laptopLoss": {
          "Nothing — it backs up automatically": 3,
          "A day's work at most": 2,
          "I copied things to a USB stick once": 1,
          "Everything. Please don't say that.": 0
        }
      },
      "ranges": {
        "/sharedPasswordCount": [
          { "max": 0, "points": 3 },
          { "max": 2, "points": 2 },
          { "max": 5, "points": 1 },
          { "points": 0 }
        ],
        "/streamingPasswordSharers": [
          { "max": 1, "points": 3 },
          { "max": 3, "points": 2 },
          { "max": 6, "points": 1 },
          { "points": 0 }
        ]
      }
    }
  ]
}
```

- [ ] **Step 2: Write the rules**

Create `demos/AIAS/agent/cyber.rules.json`:

```json
[
  {
    "actionName": "AIAS scores your answers",
    "condition": { "==": [{ "var": "checks.portraitPresent" }, false] },
    "decision": "submit",
    "payload": {
      "decision": "rejected",
      "reasonCode": "no-portrait",
      "verificationNotes": "AIAS cannot issue a Cyber Level without a face to put on it. Your Assured Identity arrived without a portrait."
    }
  },
  {
    "actionName": "AIAS scores your answers",
    "condition": { "<": [{ "var": "checks.cyberScore" }, 12] },
    "decision": "submit",
    "payload": {
      "decision": "rejected",
      "reasonCode": "cyber-fail",
      "verificationNotes": "AIAS admires the honesty, but cannot certify this. Shared passwords and ignored updates are doing you no favours."
    }
  },
  {
    "actionName": "AIAS scores your answers",
    "condition": { "<": [{ "var": "checks.cyberScore" }, 16] },
    "decision": "submit",
    "payload": {
      "decision": "approved",
      "level": "Bronze",
      "verificationNotes": "Bronze. The basics are there. The rest is habit."
    }
  },
  {
    "actionName": "AIAS scores your answers",
    "condition": { "<": [{ "var": "checks.cyberScore" }, 21] },
    "decision": "submit",
    "payload": {
      "decision": "approved",
      "level": "Silver",
      "verificationNotes": "Silver. Solid habits, with one or two things you already know about."
    }
  },
  {
    "actionName": "AIAS scores your answers",
    "condition": { "<": [{ "var": "checks.cyberScore" }, 24] },
    "decision": "submit",
    "payload": {
      "decision": "approved",
      "level": "Gold",
      "verificationNotes": "Gold. Genuinely well looked after. AIAS is quietly impressed."
    }
  },
  {
    "actionName": "AIAS scores your answers",
    "condition": { "==": [1, 1] },
    "decision": "submit",
    "payload": {
      "decision": "approved",
      "level": "Platinum",
      "verificationNotes": "Platinum. A perfect card. AIAS has notes for everyone else."
    }
  }
]
```

- [ ] **Step 3: Write the band-boundary tests**

Create `tests/Sorcha.Agent.Tests/Decision/Checks/CyberBandBoundaryTests.cs`. These read the **real** config files, so a retune that breaks a boundary fails CI:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Logic;
using Sorcha.Agent.Decision.Checks;

namespace Sorcha.Agent.Tests.Decision.Checks;

/// <summary>
/// Pins the four band transitions against the REAL demos/AIAS/agent/cyber.rules.json, so
/// retuning the scoring table cannot silently move a boundary. Feature AIAS M2.
/// </summary>
public class CyberBandBoundaryTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "demos")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new DirectoryNotFoundException("Could not locate repo root from test output directory");
    }

    private static readonly string RulesPath =
        Path.Combine(RepoRoot(), "demos", "AIAS", "agent", "cyber.rules.json");

    private static readonly string ChecksPath =
        Path.Combine(RepoRoot(), "demos", "AIAS", "agent", "cyber.checks.json");

    private static string? LevelFor(int score, bool portraitPresent = true)
    {
        var rules = JsonNode.Parse(File.ReadAllText(RulesPath))!.AsArray();
        var data = new JsonObject
        {
            ["checks"] = new JsonObject
            {
                ["portraitPresent"] = portraitPresent,
                ["cyberScore"] = score
            }
        };

        foreach (var entry in rules)
        {
            var condition = entry!["condition"]!;
            var rule = JsonSerializer.Deserialize<Rule>(condition.ToJsonString())!;
            var result = rule.Apply(data);
            if (result is JsonValue v && v.TryGetValue(out bool b) && b)
                return entry["payload"]!["level"]?.GetValue<string>() ?? "REJECTED";
        }

        return null;
    }

    [Theory]
    [InlineData(0, "REJECTED")]
    [InlineData(11, "REJECTED")]
    [InlineData(12, "Bronze")]
    [InlineData(15, "Bronze")]
    [InlineData(16, "Silver")]
    [InlineData(20, "Silver")]
    [InlineData(21, "Gold")]
    [InlineData(23, "Gold")]
    [InlineData(24, "Platinum")]
    public void Rules_AtEveryBandBoundary_AwardTheLockedLevel(int score, string expected)
    {
        LevelFor(score).Should().Be(expected);
    }

    [Fact]
    public void Rules_NoPortrait_RejectBeforeScoringEvenOnAPerfectCard()
    {
        LevelFor(24, portraitPresent: false).Should().Be("REJECTED");
    }

    [Fact]
    public void ChecksConfig_TotalAvailablePoints_Is24()
    {
        var config = ChecksConfig.Load(ChecksPath);
        var scored = config.Checks.Single(c => c.Type == "scored-questionnaire");

        var answerMax = (scored.Answers ?? []).Sum(q => q.Value.Values.Max());
        var rangeMax = (scored.Ranges ?? []).Sum(q => q.Value.Max(r => r.Points));

        (answerMax + rangeMax).Should().Be(24);
    }

    [Fact]
    public void ChecksConfig_EveryAnswerKey_ExistsInTheBlueprintEnum()
    {
        var config = ChecksConfig.Load(ChecksPath);
        var scored = config.Checks.Single(c => c.Type == "scored-questionnaire");

        var blueprint = JsonNode.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "demos", "AIAS", "blueprints", "aias-cyber-level.template.json")))!;
        var properties = blueprint["template"]!["actions"]![0]!["dataSchemas"]![0]!["properties"]!.AsObject();

        foreach (var (pointer, table) in scored.Answers ?? [])
        {
            var field = pointer.TrimStart('/');
            var enumValues = properties[field]!["enum"]!.AsArray()
                .Select(v => v!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);

            foreach (var answer in table.Keys)
                enumValues.Should().Contain(answer,
                    $"scoring key '{answer}' for {pointer} must match a blueprint enum value verbatim");
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet build src/Apps/Sorcha.Agent/Sorcha.Agent.csproj
dotnet test tests/Sorcha.Agent.Tests/Sorcha.Agent.Tests.csproj
```

Expected: PASS. If `ChecksConfig_EveryAnswerKey_ExistsInTheBlueprintEnum` fails, the checks config and blueprint have drifted — fix the config, not the test.

- [ ] **Step 5: Commit**

```bash
git add demos/AIAS/agent/cyber.checks.json demos/AIAS/agent/cyber.rules.json \
        tests/Sorcha.Agent.Tests/Decision/Checks/CyberBandBoundaryTests.cs
git commit -m "feat: [AIAS M2] - cyber scoring table, band rules, and boundary tests"
```

---

## Task 8: Provisioning — Cyber register and per-template register targeting

**Files:**
- Modify: `demos/AIAS/AiasDemo.psm1` — `New-AiasOrg` (~line 216), `Publish-AiasBlueprint` (line 319), `Build-AiasAgentConfig` (line 623), `Get-AiasDemoStatus` (line 719), `Export-ModuleMember` (line 831)

**Interfaces:**
- Consumes: the blueprint template from Task 6, the agent config trio from Task 7.
- Produces: `state.json` gains `cyberRegisterId` and `blueprintIds.cyberLevel`; `Start-AiasAgent -Mode cyber` launches the second agent.

- [ ] **Step 1: Add the Cyber register to `New-AiasOrg`**

In `New-AiasOrg`, immediately after the existing `New-SorchaRegister` call and its `Write-WtSuccess "register: ..."` line, add:

```powershell
    # M2: the cyber questionnaire lives on its OWN register, not the Identity one. Keeps the two
    # agents cleanly separated — each agent config is register-scoped, so neither can pick up the
    # other's pending actions. Name is 20 chars; the hard cap is 38 and exceeding it fails deep in
    # register finalize, which previously surfaced as an unexplained 90-second seal timeout.
    $cyberRegister = New-SorchaRegister -RegisterUrl $api -WalletUrl $api `
        -Name $script:AiasCyberName `
        -Description "AIAS Cyber Level register — owned by $($node.id)" `
        -TenantId $vOrgId -OwnerUserId $vAdmin.UserId -OwnerWalletAddress $vWallet.Address `
        -Headers $vAdmin.Headers -TenantUrl $api -DevMode:$true
    Write-WtSuccess "cyber register: $($cyberRegister.RegisterId)"

    $null = Publish-SorchaParticipant -TenantUrl $api -OrganizationId $vOrgId `
        -RegisterId $cyberRegister.RegisterId -ParticipantName "Assure-ID Agent" `
        -OrganizationName $script:AiasName -WalletAddress $agentWallet.Address `
        -PublicKey $agentWallet.PublicKey -Headers $vAdmin.Headers
```

Then add `cyberRegisterId = $cyberRegister.RegisterId` to the state hashtable written at the end of `New-AiasOrg` (alongside the existing `registerId` entry).

Near the top of the module, beside the existing `$script:AiasName`, add:

```powershell
$script:AiasCyberName = "Acme Cyber Assurance"
```

- [ ] **Step 2: Give `Publish-AiasBlueprint` a per-template register target**

Locate the loop in `Publish-AiasBlueprint` that iterates the template files. Replace the single shared `$state.registerId` usage with a per-template lookup table declared just before the loop:

```powershell
    # Each template publishes to its own register. The identity + device-binding workflows share
    # the Assured Identity register; the cyber questionnaire has its own (M2).
    $templateTargets = @(
        @{ File = "aias-assured-identity.template.json";   Key = "assuredIdentity";   RegisterId = $state.registerId }
        @{ File = "aias-device-registration.template.json"; Key = "deviceRegistration"; RegisterId = $state.registerId }
        @{ File = "aias-cyber-level.template.json";         Key = "cyberLevel";         RegisterId = $state.cyberRegisterId }
    )

    foreach ($target in $templateTargets) {
        if (-not $target.RegisterId) {
            throw "No register id for template '$($target.File)' — re-run New-AiasOrg to provision the missing register."
        }
    }
```

Then drive the existing publish body from `$target.File` / `$target.RegisterId`, recording the resulting id under `blueprintIds.$($target.Key)`.

- [ ] **Step 3: Teach `Build-AiasAgentConfig` about cyber mode**

Add a `-Mode` parameter defaulting to `assure-id`:

```powershell
    param(
        [ValidateSet('assure-id', 'cyber')]
        [string]$Mode = 'assure-id',
        # ... existing parameters unchanged ...
    )
```

and select the config path, rules/checks files, and register id from it:

```powershell
    $agentFiles = if ($Mode -eq 'cyber') {
        @{ Config = "cyber.config.json"; Rules = "cyber.rules.json"; Checks = "cyber.checks.json"; RegisterId = $state.cyberRegisterId }
    } else {
        @{ Config = "assure-id.config.json"; Rules = "assure-id.rules.json"; Checks = "assure-id.checks.json"; RegisterId = $state.registerId }
    }
```

**This is the load-bearing bit:** the two modes MUST write to different config paths. The Assure-ID agent's config path is fixed and shared, so a cyber provisioning run that reused it would clobber a running Assure-ID agent's configuration.

- [ ] **Step 4: Extend `Start-AiasAgent`**

Change the parameter block at line 467 from:

```powershell
    param(
        [string]$StateFile = (Join-Path $script:DemoRoot "state.json")
    )
```

to:

```powershell
    param(
        [string]$StateFile = (Join-Path $script:DemoRoot "state.json"),
        [ValidateSet('assure-id', 'cyber')]
        [string]$Mode = 'assure-id'
    )
```

Replace the blueprint-id warning and banner with mode-aware equivalents:

```powershell
    $expectedBlueprintKey = if ($Mode -eq 'cyber') { 'cyberLevel' } else { 'assuredIdentity' }
    $blueprintProp = $state.PSObject.Properties['blueprintIds']
    $hasBlueprint = $blueprintProp -and $blueprintProp.Value -and
                    $blueprintProp.Value.PSObject.Properties[$expectedBlueprintKey]
    if (-not $hasBlueprint) {
        Write-WtWarn "state has no '$expectedBlueprintKey' blueprint id — publish first (Publish-AiasBlueprint)."
    }

    $agentLabel = if ($Mode -eq 'cyber') { 'Cyber' } else { 'Assure-ID' }
    Write-WtBanner "AIAS demo — launch $agentLabel agent (rules)"
```

Pass the mode into the config build and record the pid under a mode-keyed state entry:

```powershell
    $configPath = Build-AiasAgentConfig -State $state -Node $node -Mode $Mode
    $checksName = if ($Mode -eq 'cyber') { 'cyber.checks.json' } else { 'assure-id.checks.json' }
    Write-WtSuccess "agent config: $configPath (checksFile=$checksName)"

    $pidKey = if ($Mode -eq 'cyber') { 'cyberAgentPid' } else { 'agentPid' }
```

Everywhere the existing body writes `agentPid`, `agentLogPath`, `agentErrorLogPath`, `agentStartedAt`, `agentConfigPath` or `agentProcessName` into state, key them off `$pidKey` and the matching `$Mode`-prefixed names so the two agents are tracked independently and `Test-AiasAgentAlive` can check each one.

- [ ] **Step 5: Extend the status probe**

In `Get-AiasDemoStatus`, add the cyber register beside the existing readable-register probe so a missing or unsealed cyber register reports NotReady with a named cause rather than surfacing later as a publish failure:

```powershell
    $cyberProp = $State.PSObject.Properties['cyberRegisterId']
    if (-not $cyberProp -or -not $cyberProp.Value) {
        $reasons += "cyber-register-missing"
    }
    elseif (-not (Test-AiasRegisterReadable -Api $api -RegisterId $cyberProp.Value -Headers $headers)) {
        $reasons += "cyber-register-not-readable"
    }
```

Use indexed `PSObject.Properties['x']` access, **not** `.Properties.Name -contains` — the latter throws on a sparse or empty `pscustomobject`, turning a partially-written `state.json` into a crash instead of a NotReady verdict (the trap #1269 already fixed once).

- [ ] **Step 6: Export the changes**

`Export-ModuleMember` needs no new names — the new capability rides existing exported functions via their `-Mode` parameter.

- [ ] **Step 7: Verify provisioning against local Docker**

```bash
pwsh -NoProfile -Command "Import-Module ./demos/AIAS/AiasDemo.psm1 -Force; Get-AiasDemoStatus"
```

Expected: reports the cyber register id and all three blueprint ids, or a named NotReady cause.

**Trap to watch for:** `AiasDemo.psm1` writes agent configs to fixed paths under `demos/AIAS/agent/`. If an n1-pointed agent is running while you provision locally, back up `assure-id.config.json` first and restore it after — the running process holds its config in memory, but the file on disk gets overwritten.

- [ ] **Step 8: Commit**

```bash
git add demos/AIAS/AiasDemo.psm1
git commit -m "feat: [AIAS M2] - provision a separate cyber register and per-template publish targets"
```

---

## Task 9: Rehearsal paths

**Files:**
- Modify: `demos/AIAS/rehearse.ps1`

**Interfaces:**
- Consumes: everything above.

- [ ] **Step 1: Add the answer fixtures**

Add a `-Scenario` parameter accepting `identity` (the existing behaviour, the default) and `cyber`, then declare the four answer sets. The point totals are stated in comments so a retune that breaks a band is obvious at the call site:

```powershell
# 24 → Platinum. Every top answer, both sliders at zero.
$script:CyberAnswersPerfect = @{
    passwordStorage          = "A password manager"
    emailSecondStep          = "Yes — an app or a hardware key"
    passwordChangeHabit      = "Only when I think one's been exposed"
    phoneUpdates             = "It installs automatically"
    suspiciousEmail          = "Ignore the email and open the bank's app myself"
    laptopLoss               = "Nothing — it backs up automatically"
    sharedPasswordCount      = 0
    streamingPasswordSharers = 0
}

# 20 → Silver. Perfect card minus BOTH traps (-2 each): the confident-but-wrong answers.
$script:CyberAnswersTrapped = $script:CyberAnswersPerfect.Clone()
$script:CyberAnswersTrapped.passwordChangeHabit = "Every 30 days, like clockwork"
$script:CyberAnswersTrapped.suspiciousEmail     = "Check the sender address carefully, then decide"

# 0 → Fail, no credential.
$script:CyberAnswersDire = @{
    passwordStorage          = "The same one everywhere, and hope"
    emailSecondStep          = "No, just the password"
    passwordChangeHabit      = "Change them?"
    phoneUpdates             = "There's a red badge I've been ignoring since spring"
    suspiciousEmail          = "Click it — it looked legitimate"
    laptopLoss               = "Everything. Please don't say that."
    sharedPasswordCount      = 10
    streamingPasswordSharers = 10
}
```

- [ ] **Step 2: Add the four assertion paths**

Each path submits action 1 with its answer set, waits for the agent to submit action 2, and asserts on the **wallet database** rather than on the script's exit code alone (the existing identity paths already read `wallet."Credentials".ClaimsJson` — reuse that helper).

| Path | Answers | Assert |
|---|---|---|
| 1 | `CyberAnswersPerfect` | `CyberLevelCredential` delivered; `ClaimsJson.level == "Platinum"`; `portrait` claim present |
| 2 | `CyberAnswersTrapped` | `CyberLevelCredential` delivered; `ClaimsJson.level == "Silver"` — proves both traps cost points |
| 3 | `CyberAnswersDire` | **No** credential delivered; citizen inbox carries a decision notice whose message matches the `cyber-fail` catalogue entry |
| 4 | `CyberAnswersPerfect`, but presenting an Assured Identity issued **without** a portrait | **No** credential delivered; inbox notice matches the `no-portrait` entry |

Path 2 is the one that proves the design's central claim — that the spread is real and the traps bite. Do not drop it for time.

- [ ] **Step 3: Raise the delivery timeout**

The existing approval-delivery assertion times out at 60s, which is too tight for a cold n1 — the credential lands correctly just past it and the script exits 1 on a successful run. Raise it to 120s, matching the fix already applied for the assured-identity path.

- [ ] **Step 4: Run the rehearsal against local Docker**

```bash
pwsh -NoProfile -File ./demos/AIAS/rehearse.ps1 -Scenario cyber
```

Expected: exit 0, three paths reported PASS.

- [ ] **Step 5: Verify the cross-register assumption explicitly**

This is the one new assumption the register split introduces — the credential was issued on the Identity register but is presented into a workflow on the Cyber register. Confirm the presentation gate clears and that `presentedCredential` reached the agent:

```bash
grep -i "presentedCredential\|External checks evaluated" demos/AIAS/logs/agent-*.log | tail -20
```

Expected: the agent's structured check log names the evaluated facts, including a non-zero `cyberScore` and `portraitPresent=true`. If `portraitPresent` is false on a portrait-bearing identity, the disclosure clamp is not carrying `presentedCredential` — stop and investigate before proceeding.

- [ ] **Step 6: Update task tracking and docs**

- `.specify/MASTER-TASKS.md` — add the AIAS M2 row, status ✅.
- `.claude/skills/sorcha-architecture/SKILL.md` — add a short "AIAS Cyber Level (M2)" section covering the `scored-questionnaire` check, the numeric-fact contract, and the two-register topology.
- `demos/AIAS/README.md` — document the cyber scenario, the second agent mode, and the two registers.

- [ ] **Step 7: Commit and open the PR**

```bash
git add demos/AIAS/rehearse.ps1 demos/AIAS/README.md \
        .specify/MASTER-TASKS.md .claude/skills/sorcha-architecture/SKILL.md
git commit -m "feat: [AIAS M2] - cyber rehearsal paths and documentation"
git push -u origin feature/aias-m2-demo-assets
gh pr create --fill
```

---

## Verification checklist

Before claiming M2 complete, all of these must have been run and observed passing:

- [ ] `dotnet build` clean across the solution
- [ ] `dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj` — slider inference + renderer seeding
- [ ] `dotnet test tests/Sorcha.Agent.Tests/Sorcha.Agent.Tests.csproj` — scorer, numeric facts, band boundaries, config/blueprint agreement
- [ ] `dotnet test tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj` — optional claims
- [ ] `rehearse.ps1 -Scenario cyber` exits 0 with all four paths PASS
- [ ] Agent log confirms `presentedCredential` crossed the register boundary (Task 9 Step 5)
- [ ] A Platinum and a Silver run both observed, proving the spread is real and both traps bite
