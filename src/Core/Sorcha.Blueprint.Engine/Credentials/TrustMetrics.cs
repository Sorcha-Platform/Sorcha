// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// OpenTelemetry meter for unified trust decisions (feature 135). Instruments record the
/// outcome, deciding source, credential format, and assurance level of each trust decision
/// — never credential subject data (FR-024). Instrument bodies are added with the evaluator
/// in User Story 1; this type fixes the meter name so dashboards/registration can bind early.
/// </summary>
public static class TrustMetrics
{
    /// <summary>The meter name used for all trust-decision instruments.</summary>
    public const string MeterName = "Sorcha.Trust";
}
