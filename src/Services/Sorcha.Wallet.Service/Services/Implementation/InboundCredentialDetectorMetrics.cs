// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Feature 106 — OpenTelemetry metrics for <c>InboundCredentialDetector</c>. Each
/// bloom-filter hit runs the detector exactly once; these counters report what
/// happened. Extraction latency helps spot pipeline regressions in the cross-node
/// delivery path.
/// </summary>
/// <remarks>
/// Contract: <c>specs/106-register-native-credentials/contracts/inbound-credential-detection.md §Metrics</c>.
/// </remarks>
public sealed class InboundCredentialDetectorMetrics
{
    public const string MeterName = "Sorcha.Wallet.InboundCredentialDetection";

    private readonly Counter<long> _inspected;
    private readonly Counter<long> _extracted;
    private readonly Counter<long> _skippedNoRecipientDisclosure;
    private readonly Counter<long> _skippedDecryptFailed;
    private readonly Counter<long> _skippedDuplicate;
    private readonly Counter<long> _errored;
    private readonly Histogram<double> _extractionLatencyMs;

    public InboundCredentialDetectorMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _inspected = meter.CreateCounter<long>(
            "sorcha.wallet.inbound_credential.inspected",
            description: "Total inbound transactions inspected by the credential detector");

        _extracted = meter.CreateCounter<long>(
            "sorcha.wallet.inbound_credential.extracted",
            description: "Credentials successfully extracted and persisted as PendingAcceptance");

        _skippedNoRecipientDisclosure = meter.CreateCounter<long>(
            "sorcha.wallet.inbound_credential.skipped_no_recipient_disclosure",
            description: "Transactions skipped because they carried no recipient-addressed credential disclosure");

        _skippedDecryptFailed = meter.CreateCounter<long>(
            "sorcha.wallet.inbound_credential.skipped_decrypt_failed",
            description: "Recipient disclosure present but decryption failed (wrong key / tampered payload)");

        _skippedDuplicate = meter.CreateCounter<long>(
            "sorcha.wallet.inbound_credential.skipped_duplicate",
            description: "Credential id already exists in the local store — idempotent replay");

        _errored = meter.CreateCounter<long>(
            "sorcha.wallet.inbound_credential.errored",
            description: "Unhandled exception during extraction (dependency throw caught by catch-all)");

        _extractionLatencyMs = meter.CreateHistogram<double>(
            "sorcha.wallet.inbound_credential.extraction_latency_ms",
            unit: "ms",
            description: "End-to-end latency from TryExtractAsync entry to persisted row");
    }

    public void RecordInspected() => _inspected.Add(1);
    public void RecordExtracted() => _extracted.Add(1);
    public void RecordSkippedNoRecipientDisclosure() => _skippedNoRecipientDisclosure.Add(1);
    public void RecordSkippedDecryptFailed() => _skippedDecryptFailed.Add(1);
    public void RecordSkippedDuplicate() => _skippedDuplicate.Add(1);
    public void RecordErrored() => _errored.Add(1);
    public void RecordExtractionLatency(double latencyMs) => _extractionLatencyMs.Record(latencyMs);
}
