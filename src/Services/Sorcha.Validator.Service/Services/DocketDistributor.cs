// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Distributes dockets to peer validators and submits confirmed dockets to Register Service.
/// </summary>
public class DocketDistributor : IDocketDistributor
{
    private readonly IPeerServiceClient _peerClient;
    private readonly IRegisterServiceClient _registerClient;
    private readonly IReceiptGenerator _receiptGenerator;
    private readonly DocketDistributorConfiguration _config;
    private readonly ILogger<DocketDistributor> _logger;

    // Statistics
    private long _totalProposedBroadcasts;
    private long _totalConfirmedBroadcasts;
    private long _totalRegisterSubmissions;
    private long _failedRegisterSubmissions;
    private readonly List<double> _broadcastTimes = new();
    private readonly object _statsLock = new();
    private DateTimeOffset? _lastBroadcastAt;

    public DocketDistributor(
        IPeerServiceClient peerClient,
        IRegisterServiceClient registerClient,
        IReceiptGenerator receiptGenerator,
        IOptions<DocketDistributorConfiguration> config,
        ILogger<DocketDistributor> logger)
    {
        _peerClient = peerClient ?? throw new ArgumentNullException(nameof(peerClient));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _receiptGenerator = receiptGenerator ?? throw new ArgumentNullException(nameof(receiptGenerator));
        _config = config?.Value ?? new DocketDistributorConfiguration();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<int> BroadcastProposedDocketAsync(
        Docket docket,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docket);

        var sw = Stopwatch.StartNew();
        var peerCount = 0;

        try
        {
            _logger.LogInformation(
                "Broadcasting proposed docket {DocketNumber} for register {RegisterId}",
                docket.DocketNumber, docket.RegisterId);

            // Serialize docket for network transmission
            var docketData = DocketSerializer.SerializeToBytes(docket);

            _logger.LogDebug(
                "Serialized docket {DocketId} to {Size} bytes",
                docket.DocketId, docketData.Length);

            // Query active validators for the register
            var validators = await _peerClient.QueryValidatorsAsync(docket.RegisterId, ct);
            peerCount = validators.Count;

            if (peerCount == 0)
            {
                _logger.LogWarning(
                    "No peer validators found for register {RegisterId}",
                    docket.RegisterId);
                return 0;
            }

            // Broadcast to peer network
            await _peerClient.PublishProposedDocketAsync(
                docket.RegisterId,
                docket.DocketId,
                docketData,
                ct);

            sw.Stop();

            Interlocked.Increment(ref _totalProposedBroadcasts);
            RecordBroadcastTime(sw.Elapsed.TotalMilliseconds);
            _lastBroadcastAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Broadcast proposed docket {DocketNumber} to {PeerCount} peers in {ElapsedMs}ms",
                docket.DocketNumber, peerCount, sw.ElapsedMilliseconds);

            return peerCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to broadcast proposed docket {DocketNumber} for register {RegisterId}",
                docket.DocketNumber, docket.RegisterId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<int> BroadcastConfirmedDocketAsync(
        Docket docket,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docket);

        var sw = Stopwatch.StartNew();
        var peerCount = 0;

        try
        {
            _logger.LogInformation(
                "Broadcasting confirmed docket {DocketNumber} for register {RegisterId} with {VoteCount} votes",
                docket.DocketNumber, docket.RegisterId, docket.Votes.Count);

            // Serialize docket with consensus signatures for network transmission
            var docketData = DocketSerializer.SerializeToBytes(docket);

            _logger.LogDebug(
                "Serialized confirmed docket {DocketId} to {Size} bytes",
                docket.DocketId, docketData.Length);

            // Query active validators for the register
            var validators = await _peerClient.QueryValidatorsAsync(docket.RegisterId, ct);
            peerCount = validators.Count;

            // Broadcast to peer network
            await _peerClient.BroadcastConfirmedDocketAsync(
                docket.RegisterId,
                docket.DocketId,
                docketData,
                ct);

            sw.Stop();

            Interlocked.Increment(ref _totalConfirmedBroadcasts);
            RecordBroadcastTime(sw.Elapsed.TotalMilliseconds);
            _lastBroadcastAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Broadcast confirmed docket {DocketNumber} to {PeerCount} peers in {ElapsedMs}ms",
                docket.DocketNumber, peerCount, sw.ElapsedMilliseconds);

            return peerCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to broadcast confirmed docket {DocketNumber} for register {RegisterId}",
                docket.DocketNumber, docket.RegisterId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SubmitToRegisterServiceAsync(
        Docket docket,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docket);

        try
        {
            _logger.LogInformation(
                "Submitting confirmed docket {DocketNumber} to Register Service for register {RegisterId}",
                docket.DocketNumber, docket.RegisterId);

            // Convert to Register Service model
            var registerDocket = DocketSerializer.ToRegisterModel(docket);

            // Submit to Register Service
            var success = await _registerClient.WriteDocketAsync(registerDocket, ct);

            if (success)
            {
                Interlocked.Increment(ref _totalRegisterSubmissions);
                _logger.LogInformation(
                    "Successfully submitted docket {DocketNumber} to Register Service",
                    docket.DocketNumber);

                // Generate and submit receipts for all transactions in the docket
                try
                {
                    var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket, ct);
                    if (receipts.Length > 0)
                    {
                        await _registerClient.WriteReceiptBatchAsync(
                            docket.RegisterId, docket.DocketNumber, receipts, ct);
                    }
                }
                catch (Exception ex)
                {
                    // Receipt generation failure should not fail the docket submission
                    _logger.LogWarning(ex,
                        "Failed to generate/submit receipts for docket {DocketNumber}",
                        docket.DocketNumber);
                }
            }
            else
            {
                Interlocked.Increment(ref _failedRegisterSubmissions);
                _logger.LogWarning(
                    "Register Service rejected docket {DocketNumber}",
                    docket.DocketNumber);
            }

            return success;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedRegisterSubmissions);
            _logger.LogError(
                ex,
                "Failed to submit docket {DocketNumber} to Register Service",
                docket.DocketNumber);
            return false;
        }
    }

    /// <inheritdoc/>
    public DocketDistributorStats GetStats()
    {
        double avgBroadcastTime;
        lock (_statsLock)
        {
            avgBroadcastTime = _broadcastTimes.Count > 0 ? _broadcastTimes.Average() : 0;
        }

        return new DocketDistributorStats
        {
            TotalProposedBroadcasts = Interlocked.Read(ref _totalProposedBroadcasts),
            TotalConfirmedBroadcasts = Interlocked.Read(ref _totalConfirmedBroadcasts),
            TotalRegisterSubmissions = Interlocked.Read(ref _totalRegisterSubmissions),
            FailedRegisterSubmissions = Interlocked.Read(ref _failedRegisterSubmissions),
            AverageBroadcastTimeMs = avgBroadcastTime,
            LastBroadcastAt = _lastBroadcastAt
        };
    }

    private void RecordBroadcastTime(double timeMs)
    {
        lock (_statsLock)
        {
            _broadcastTimes.Add(timeMs);
            // Keep only last 100 samples
            if (_broadcastTimes.Count > 100)
            {
                _broadcastTimes.RemoveAt(0);
            }
        }
    }
}
