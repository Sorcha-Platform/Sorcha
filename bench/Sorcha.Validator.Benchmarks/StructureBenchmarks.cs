// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Cryptography.Interfaces;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Diagnostics;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Benchmarks;

/// <summary>
/// Pure-compute synchronous validator paths. No I/O, no async overhead — the
/// numbers here are the floor on per-rule cost in this engine.
/// </summary>
/// <remarks>
/// Structure / Timing are the rules that fire on EVERY transaction regardless
/// of whether downstream sections short-circuit. They dominate the per-tx
/// non-I/O cost. PayloadHash compute cost scales with payload size — included
/// at two sizes to surface the slope.
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class StructureBenchmarks
{
    private ValidationEngine _engine = null!;
    private Transaction _minimalTx = null!;
    private Transaction _mediumTx = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Telemetry off — we want pure validator cost, not the gated overhead.
        RuleTelemetry.SetEnabled(false);

        var config = Options.Create(new ValidationEngineConfiguration
        {
            EnableSchemaValidation = false,
            EnableSignatureVerification = false,
            EnableChainValidation = false,
            EnableBlueprintConformance = false,
            EnableFileReferenceValidation = false,
            EnableGovernanceValidation = false,
            EnableCryptoPolicyValidation = false,
        });

        var blueprintCache = Mock.Of<IBlueprintCache>();
        var hashProvider = Mock.Of<IHashProvider>();
        var cryptoModule = Mock.Of<ICryptoModule>();
        var walletUtilities = Mock.Of<IWalletUtilities>();
        var registerClient = Mock.Of<IRegisterServiceClient>();
        var rights = Mock.Of<IRightsEnforcementService>();

        _engine = new ValidationEngine(
            config,
            blueprintCache,
            hashProvider,
            cryptoModule,
            walletUtilities,
            registerClient,
            rights,
            NullLogger<ValidationEngine>.Instance);

        _minimalTx = TransactionFixture.Minimal();
        _mediumTx = TransactionFixture.MediumPayload();
    }

    [Benchmark]
    public ValidationEngineResult ValidateStructure_Minimal()
        => _engine.ValidateStructure(_minimalTx);

    [Benchmark]
    public ValidationEngineResult ValidateStructure_Medium()
        => _engine.ValidateStructure(_mediumTx);
}
