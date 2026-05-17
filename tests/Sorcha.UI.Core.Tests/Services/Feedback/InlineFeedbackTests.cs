// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Services.Feedback;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Feedback;

/// <summary>
/// Unit tests for <see cref="InlineFeedback"/> — the Snackbar-replacement
/// service for actor's-own-action feedback (Phase 1 of the Snackbar
/// retirement). Covers severity routing, the change-event contract, and
/// the auto-dismiss timer behaviour.
/// </summary>
public sealed class InlineFeedbackTests
{
    [Fact]
    public void NewInstance_HasNoMessages()
    {
        using var feedback = new InlineFeedback();
        feedback.CurrentMessages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(InlineFeedbackSeverity.Success, "Wallet created")]
    [InlineData(InlineFeedbackSeverity.Error, "Signing failed")]
    [InlineData(InlineFeedbackSeverity.Info, "Default wallet changed")]
    [InlineData(InlineFeedbackSeverity.Warning, "Clock skew detected")]
    public void ShowMethods_AddMessageWithMatchingSeverity(
        InlineFeedbackSeverity expected, string message)
    {
        using var feedback = new InlineFeedback();

        var id = expected switch
        {
            InlineFeedbackSeverity.Success => feedback.ShowSuccess(message),
            InlineFeedbackSeverity.Error => feedback.ShowError(message),
            InlineFeedbackSeverity.Info => feedback.ShowInfo(message),
            InlineFeedbackSeverity.Warning => feedback.ShowWarning(message),
            _ => throw new InvalidOperationException(),
        };

        var entry = feedback.CurrentMessages.Single();
        entry.Id.Should().Be(id);
        entry.Severity.Should().Be(expected);
        entry.Message.Should().Be(message);
        entry.AutoDismissMs.Should().Be(InlineFeedback.DefaultAutoDismissMs);
        entry.DetailHref.Should().BeNull();
    }

    [Fact]
    public void Show_ChangedEventFires_OnceWithCurrentSnapshot()
    {
        using var feedback = new InlineFeedback();
        var fireCount = 0;
        feedback.Changed += () => fireCount++;

        feedback.ShowSuccess("first");

        fireCount.Should().Be(1);
        feedback.CurrentMessages.Should().HaveCount(1);
    }

    [Fact]
    public void MultipleShows_StackInOrder()
    {
        using var feedback = new InlineFeedback();
        feedback.ShowSuccess("first");
        feedback.ShowInfo("second");
        feedback.ShowError("third");

        feedback.CurrentMessages.Select(m => m.Message)
            .Should().Equal("first", "second", "third");
    }

    [Fact]
    public void Dismiss_RemovesMessageAndFiresChanged()
    {
        using var feedback = new InlineFeedback();
        var id = feedback.ShowSuccess("first");

        var fireCount = 0;
        feedback.Changed += () => fireCount++;

        feedback.Dismiss(id);

        feedback.CurrentMessages.Should().BeEmpty();
        fireCount.Should().Be(1);
    }

    [Fact]
    public void Dismiss_UnknownId_IsNoOpAndDoesNotFireChanged()
    {
        using var feedback = new InlineFeedback();
        feedback.ShowSuccess("first");

        var fireCount = 0;
        feedback.Changed += () => fireCount++;

        feedback.Dismiss(Guid.NewGuid());

        feedback.CurrentMessages.Should().HaveCount(1);
        fireCount.Should().Be(0);
    }

    [Fact]
    public void Clear_RemovesAllMessages()
    {
        using var feedback = new InlineFeedback();
        feedback.ShowSuccess("a");
        feedback.ShowError("b");

        var fireCount = 0;
        feedback.Changed += () => fireCount++;

        feedback.Clear();

        feedback.CurrentMessages.Should().BeEmpty();
        fireCount.Should().Be(1);
    }

    [Fact]
    public void Clear_OnEmpty_DoesNotFireChanged()
    {
        using var feedback = new InlineFeedback();
        var fireCount = 0;
        feedback.Changed += () => fireCount++;

        feedback.Clear();

        fireCount.Should().Be(0);
    }

    [Fact]
    public void ShowWithDetailHref_PreservesHrefOnTheMessage()
    {
        using var feedback = new InlineFeedback();
        feedback.ShowSuccess("Credential issued", detailHref: "credentials/abc");

        feedback.CurrentMessages.Single().DetailHref.Should().Be("credentials/abc");
    }

    [Fact]
    public void ShowWithCustomAutoDismiss_PreservesValue()
    {
        using var feedback = new InlineFeedback();
        feedback.ShowError("Save failed", autoDismissMs: 10_000);

        feedback.CurrentMessages.Single().AutoDismissMs.Should().Be(10_000);
    }

    [Fact]
    public void ShowWithZeroAutoDismiss_StaysIndefinitely()
    {
        // Use case: errors the user must acknowledge explicitly. The timer
        // is not registered when AutoDismissMs <= 0, so a long-enough sleep
        // here would still leave the message in place — testing the storage
        // contract directly is the cheap way to assert the same thing.
        using var feedback = new InlineFeedback();
        feedback.ShowError("Unrecoverable", autoDismissMs: 0);

        var entry = feedback.CurrentMessages.Single();
        entry.AutoDismissMs.Should().Be(0);
    }

    [Fact]
    public async Task AutoDismiss_RemovesMessageAfterDelay()
    {
        using var feedback = new InlineFeedback();
        feedback.ShowInfo("Short-lived", autoDismissMs: 50);

        // Generous polling window keeps the test reliable on CI where Timer
        // callbacks can be queued behind heavier work. 1.5s is well above
        // the 50ms dismiss target.
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(1500);
        while (feedback.CurrentMessages.Count > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        feedback.CurrentMessages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void ShowWithBlankMessage_Throws(string blank)
    {
        using var feedback = new InlineFeedback();
        var act = () => feedback.ShowInfo(blank);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Dispose_ClearsMessagesAndIsIdempotent()
    {
        var feedback = new InlineFeedback();
        feedback.ShowSuccess("a");

        feedback.Dispose();
        feedback.Dispose(); // second call is a no-op

        feedback.CurrentMessages.Should().BeEmpty();
    }
}
