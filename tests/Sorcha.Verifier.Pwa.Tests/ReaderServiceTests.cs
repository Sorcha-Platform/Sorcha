// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Verifier.Pwa.Services;
using Xunit;

namespace Sorcha.Verifier.Pwa.Tests;

/// <summary>The reader's engagement decoding, and the questions it is allowed to ask.</summary>
public class ReaderServiceTests
{
    /// <summary>
    /// A reader that will "have a go" at any QR is a reader that can be pointed at something else entirely.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/pay?amount=500")]
    [InlineData("openid4vp://authorize?request_uri=…")]
    [InlineData("just some text")]
    [InlineData("")]
    [InlineData("   ")]
    public void ANonEngagementQr_IsRefused(string payload)
    {
        var act = () => ReaderService.DecodeEngagement(payload);

        act.Should().Throw<Exception>(because:
            "the reader must refuse anything that is not an engagement code — scanning an arbitrary QR and " +
            "attempting to speak mdoc at it is how a reader gets pointed at the wrong thing");
    }

    /// <summary>A damaged engagement code fails with something a person can act on.</summary>
    [Fact]
    public void ADamagedEngagementCode_FailsWithUsefulAdvice()
    {
        var act = () => ReaderService.DecodeEngagement("mdoc:!!!not-base64!!!");

        act.Should().Throw<FormatException>()
            .WithMessage("*show it again*", because:
                "'Ask them to show it again' is actionable at a counter; 'invalid base64' is not");
    }

    /// <summary>A well-formed engagement code decodes to its bytes.</summary>
    [Fact]
    public void AWellFormedEngagementCode_Decodes()
    {
        var payload = new byte[] { 0xa3, 0x00, 0x63, 0x31, 0x2e, 0x30 };
        var qr = "mdoc:" + Convert.ToBase64String(payload)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        ReaderService.DecodeEngagement(qr).Should().Equal(payload);
    }

    /// <summary>
    /// "Are they over 18?" must ask for the <b>answer</b>, and never for the birth date it could be derived
    /// from.
    /// </summary>
    /// <remarks>
    /// This is the whole reason an age-attestation element exists in the standard. A reader that asks for
    /// <c>birth_date</c> to answer an age question has learned the citizen's exact date of birth — and
    /// everything derivable from it — to answer a yes/no. The preset is what makes the minimal ask the easy
    /// ask, and this test is what stops it quietly growing.
    /// </remarks>
    [Fact]
    public void TheAgeQuestion_AsksForTheAnswer_NotTheBirthDate()
    {
        var elements = ReaderQuestion.OverEighteen.Elements;

        elements.Should().ContainSingle();
        elements[0].ElementIdentifier.Should().Be("age_over_18");

        elements.Should().NotContain(e => e.ElementIdentifier == "birth_date", because:
            "asking for a date of birth to answer 'are they over 18' learns vastly more than the question needs");
    }

    /// <summary>An age check keeps nothing. There is nothing to keep — the answer is the whole point.</summary>
    [Fact]
    public void TheAgeQuestion_RetainsNothing()
    {
        ReaderQuestion.OverEighteen.Elements.Should().OnlyContain(e => !e.IntentToRetain);
    }

    /// <summary>
    /// A question that <em>does</em> keep a record declares it truthfully — so the citizen can see it before
    /// consenting.
    /// </summary>
    /// <remarks>
    /// Declaring <c>intentToRetain</c> honestly is the entire reason the flag exists. A reader that quietly
    /// kept data it declared it would not keep would be worse than one that never asked.
    /// </remarks>
    [Fact]
    public void AQuestionThatKeepsARecord_SaysSo()
    {
        ReaderQuestion.LicenceOnRecord.Elements.Should().OnlyContain(e => e.IntentToRetain);
    }

    /// <summary>Identity confirmation is a look, not a filing. It keeps nothing.</summary>
    [Fact]
    public void ConfirmingIdentity_KeepsNothing()
    {
        ReaderQuestion.ConfirmIdentity.Elements.Should().OnlyContain(e => !e.IntentToRetain);
    }
}
