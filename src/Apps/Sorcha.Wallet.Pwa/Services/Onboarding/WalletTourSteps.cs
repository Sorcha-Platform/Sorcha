// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Components.User.Components.Onboarding;

namespace Sorcha.Wallet.Pwa.Services.Onboarding;

/// <summary>
/// Wallet-specific guided-tour content (Feature 125, T105). Plain-English
/// copy at or below Year 8 reading age per SC-010, covering the four
/// destinations a first-time user needs to know about.
/// </summary>
public static class WalletTourSteps
{
    /// <summary>Default v1 tour steps for the Sorcha Wallet.</summary>
    public static IReadOnlyList<GuidedTourScaffold.GuidedTourStep> Default { get; } = new[]
    {
        new GuidedTourScaffold.GuidedTourStep(
            Title: "Welcome to your Sorcha Wallet",
            Body: "This is your in-pocket home for credentials, applications, and identity. Let's walk through the four things you'll use most."),
        new GuidedTourScaffold.GuidedTourStep(
            Title: "Show a credential",
            Body: "Tap Present when someone asks to see your credential. The wallet picks the right one and asks you to confirm before sharing."),
        new GuidedTourScaffold.GuidedTourStep(
            Title: "Check someone else's credential",
            Body: "Tap Verify to scan or paste a credential someone shows you. The wallet checks it and tells you in plain English whether you can trust it. Useful at the doorstep."),
        new GuidedTourScaffold.GuidedTourStep(
            Title: "Switch contexts",
            Body: "If you belong to more than one organisation, tap the chip at the top to switch between them. Your credentials, applications, and details swap to match."),
        new GuidedTourScaffold.GuidedTourStep(
            Title: "Find everything in Settings",
            Body: "The footer holds Devices, Activity, and Settings. Settings is the hub for your profile, sign-in methods, and a Replay tour button if you ever want this walk-through again.")
    };
}
