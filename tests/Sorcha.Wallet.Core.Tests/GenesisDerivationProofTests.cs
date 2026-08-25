// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Wallet.Contracts.Constants;
using Sorcha.Wallet.Core.Encryption.Interfaces;
using Sorcha.Wallet.Core.Encryption.Providers;
using Sorcha.Wallet.Core.Services.Implementation;
using Xunit;
using WalletMnemonic = Sorcha.Wallet.Core.Domain.ValueObjects.Mnemonic;
using WalletDerivationPath = Sorcha.Wallet.Core.Domain.ValueObjects.DerivationPath;

namespace Sorcha.Wallet.Core.Tests;

/// <summary>
/// Numerical regression proof for the federation deadlock (#461 phase 4): the genesis ceremony's
/// docket-signing public key MUST equal the key the wallet-service runtime derives for the same
/// mnemonic. If the two chains diverge, the validator never matches its own roster entry,
/// docket 0 never seals, and nothing anywhere says why.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two of the three tests run everywhere, including CI, and that is the point of this file's
/// current shape.</b> It previously consisted of a single test that read the real ceremony output
/// from <c>temp/genesis-validator-key.json</c> — gitignored private key material, therefore absent
/// on every CI machine — and <em>returned early</em> when it could not find it. So it passed
/// vacuously on every CI run from #1378 onward while the genesis payload had moved
/// <c>validators</c> to <c>roster</c> underneath it (#1501), and hard-failed with a bare
/// <c>KeyNotFoundException</c> on the one machine that had done a ceremony (#1454).
/// </para>
/// <para>
/// The invariant does not actually need the real ceremony key: it is a property of the two
/// derivation chains, and a randomly generated mnemonic exercises it identically. The real-genesis
/// arm is kept as a belt-and-braces check on the artefact that is actually deployed — and it now
/// distinguishes <b>inputs absent</b> (legitimately skip) from <b>inputs present but unrecognised</b>
/// (fail loudly: the genesis format moving underneath a proof test is the one thing this file exists
/// to notice).
/// </para>
/// </remarks>
public class GenesisDerivationProofTests
{
    /// <summary>
    /// The BIP44 path the CLI ceremony and the wallet-service runtime must agree on. Written out
    /// literally here on purpose — a test that reads the constant it is guarding cannot detect the
    /// constant changing, which is the drift this pins.
    /// </summary>
    private const string DocketSigningPathLiteral = "m/44'/0'/0'/0/102";

    // ---------------------------------------------------------------------------------------
    // 1 + 2: the invariant, provable from a random mnemonic — runs in CI.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void DocketSigningPath_IsTheCanonicalConstant()
    {
        SorchaDerivationPaths.DocketSigningPath.Should().Be(DocketSigningPathLiteral,
            "the genesis roster's docket-signing key is derived at this path by the CLI ceremony and " +
            "re-derived at it by the validator at runtime; changing it strands every existing network");

        SorchaDerivationPaths.ResolvePath(SorchaDerivationPaths.DocketSigning)
            .Should().Be(DocketSigningPathLiteral,
                "ResolvePath is what the runtime sign path calls — it must land on the same path the " +
                "ceremony used, or the validator derives a different but perfectly valid key and " +
                "dockets silently stop sealing");
    }

    [Fact]
    public async Task CeremonyChain_AndWalletRuntimeChain_DeriveTheSameDocketSigningKey()
    {
        // A fresh 24-word mnemonic — the same shape the ceremony mints. No secret is involved, so
        // this arm runs on every machine including CI.
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.TwentyFour);
        var crypto = new CryptoModule();

        // --- The CLI ceremony chain (Sorcha.Cli SystemRegisterCommands.ExecuteCreateAsync) -------
        // mnemonic.DeriveExtKey() -> Derive(path) -> 32-byte scalar -> ED25519 key set.
        var ceremonySeed = mnemonic.DeriveExtKey()
            .Derive(new KeyPath(SorchaDerivationPaths.DocketSigningPath))
            .PrivateKey.ToBytes();
        var ceremonyKey = await crypto.GenerateKeySetAsync(WalletNetworks.ED25519, seed: ceremonySeed);
        ceremonyKey.IsSuccess.Should().BeTrue("the ceremony's own key generation must succeed");
        var ceremonyPubKey = Convert.ToBase64String(ceremonyKey.Value!.PublicKey.Key!);

        // --- The wallet-service runtime chain ----------------------------------------------------
        // Driven through the REAL KeyManagementService rather than a hand-copied re-derivation, so
        // a change to how the runtime derives (an extra HMAC, a different seed input, a different
        // algorithm mapping) breaks this test rather than sailing past it.
        var keyManagement = new KeyManagementService(
            (IKeyProtectionProvider)new LocalEncryptionProvider(Mock.Of<ILogger<LocalEncryptionProvider>>()),
            crypto,
            Mock.Of<IWalletUtilities>(),
            Mock.Of<ILogger<KeyManagementService>>());

        // What WalletManager actually feeds DeriveKeyAtPathAsync is the 64-byte BIP39 PBKDF2 seed:
        // wallet creation stores `mnemonic.DeriveBip39Seed()` in EncryptedMasterKeyBlob
        // (WalletManager.cs:400) and the direct-master sign path decrypts exactly that blob
        // (WalletManager.cs:803).
        //
        // NOT DeriveMasterKeyAsync. Despite the name, it returns Mnemonic.DeriveSeed() — the
        // 32-byte master ExtKey PRIVATE KEY, one HMAC further along — so feeding it here adds an
        // extra HMAC round and yields a different, perfectly valid, wrong key. That is the legacy
        // pre-#471 chain, and it is why this assertion first went red against a correct platform.
        var bip39Seed = new WalletMnemonic(mnemonic.ToString()).DeriveBip39Seed();
        var (_, runtimePublicKey) = await keyManagement.DeriveKeyAtPathAsync(
            bip39Seed,
            new WalletDerivationPath(SorchaDerivationPaths.ResolvePath(SorchaDerivationPaths.DocketSigning)),
            "ED25519");
        var runtimePubKey = Convert.ToBase64String(runtimePublicKey);

        runtimePubKey.Should().Be(ceremonyPubKey,
            "the genesis ceremony writes the ceremony-chain public key into the validator roster and " +
            "the validator signs dockets with the runtime-chain key — when they diverge the validator " +
            "cannot match its own roster entry, docket 0 never seals, and no error names the cause");
    }

    // ---------------------------------------------------------------------------------------
    // 3: the deployed artefact, when this machine has actually run a ceremony.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task WalletServiceRuntimeChain_MatchesGenesisRosterPubKey()
    {
        var found = FindGenesisKeyFile();
        if (found is null)
        {
            // Legitimately absent: no ceremony has been run on this machine (every CI box, most
            // checkouts). The invariant itself is covered by the two tests above, which always run.
            return;
        }

        var (keyPath, repoRoot) = found.Value;
        var genesisPath = Path.Combine(repoRoot,
            "src", "Common", "Sorcha.Register.Models", "Resources", "system-register-genesis.json");

        // The embedded genesis is committed, so if the key file resolved a repo root the genesis
        // MUST be there. Its absence means the search found the wrong root — which used to be
        // silently indistinguishable from "no ceremony here".
        File.Exists(genesisPath).Should().BeTrue(
            $"the ceremony key resolved repo root '{repoRoot}' but the committed genesis resource is " +
            "not under it — the ancestor search found the wrong root, and returning null here is how " +
            "this test previously stopped running without saying so");

        using var keyDoc = JsonDocument.Parse(File.ReadAllText(keyPath));
        var mnemonicWords = keyDoc.RootElement.GetProperty("mnemonic").GetString()!;
        var expectedRosterPubKey = ReadRosterDocketSigningPubKey(File.ReadAllText(genesisPath));

        var crypto = new CryptoModule();
        var docketSigningSeed = new Mnemonic(mnemonicWords).DeriveExtKey()
            .Derive(new KeyPath(SorchaDerivationPaths.DocketSigningPath))
            .PrivateKey.ToBytes();
        var docketKey = await crypto.GenerateKeySetAsync(WalletNetworks.ED25519, seed: docketSigningSeed);
        docketKey.IsSuccess.Should().BeTrue();

        var pubBase64 = Convert.ToBase64String(docketKey.Value!.PublicKey.Key!);
        Console.WriteLine($"Wallet-runtime chain pubkey: {pubBase64}");
        Console.WriteLine($"Genesis roster pubkey:       {expectedRosterPubKey}");

        pubBase64.Should().Be(expectedRosterPubKey,
            "the ceremony key on this machine must match the embedded genesis it minted — a mismatch " +
            "means the two are from different ceremonies, and importing this key would leave the " +
            "validator off its own roster with docket 0 never sealing");
    }

    // ---------------------------------------------------------------------------------------
    // 4: the roster reader itself. The silent-skip it replaced is what hid #1454/#1501 for
    // months, so the fail-loud arm gets its own tests rather than being taken on trust — it
    // cannot run on a machine whose genesis is well-formed, which is every machine.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void RosterReader_AcceptsTheCurrentEnvelopeShape()
    {
        var genesis = GenesisWithControlPayload(
            """{"version":1,"roster":{"validators":{"validators":[{"publicKey":"Q1VSUkVOVA=="}]}}}""");

        ReadRosterDocketSigningPubKey(genesis).Should().Be("Q1VSUkVOVA==");
    }

    [Fact]
    public void RosterReader_AcceptsThePreF189FlatShape()
    {
        // A ceremony output predating #1378 is a legitimate thing to meet on an older machine.
        var genesis = GenesisWithControlPayload(
            """{"version":1,"validators":{"validators":[{"publicKey":"TEVHQUNZ"}]}}""");

        ReadRosterDocketSigningPubKey(genesis).Should().Be("TEVHQUNZ");
    }

    [Fact]
    public void RosterReader_ThrowsOnAnUnrecognisedShape_RatherThanSkipping()
    {
        // This is the exact condition that arose when the payload moved validators -> roster: a
        // present, parseable, well-formed genesis whose roster this reader cannot find. Returning
        // null (or skipping) here is what let a broken proof test report green for months.
        var genesis = GenesisWithControlPayload("""{"version":1,"somethingElse":{}}""");

        var act = () => ReadRosterDocketSigningPubKey(genesis);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*roster.validators.validators[0].publicKey*")
            .WithMessage("*version, somethingElse*",
                "the message must name what it actually saw — the whole cost of the original bug " +
                "was that nobody could tell a skipped test from a passing one");
    }

    [Fact]
    public void RosterReader_ThrowsWhenTheRosterIsPresentButEmpty()
    {
        // Structurally right, semantically useless: a roster with no validators cannot pin
        // anything, and quietly reading past it would be the same vacuous pass in a new shape.
        var genesis = GenesisWithControlPayload(
            """{"version":1,"roster":{"validators":{"validators":[]}}}""");

        var act = () => ReadRosterDocketSigningPubKey(genesis);

        act.Should().Throw<InvalidOperationException>();
    }

    private static string GenesisWithControlPayload(string controlPayloadJson)
    {
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(controlPayloadJson));
        return "{\"genesisTransaction\":{\"payload\":\"" + b64 + "\"}}";
    }

    /// <summary>
    /// Extracts the docket-signing public key from a genesis file's control payload.
    /// </summary>
    /// <remarks>
    /// Accepts both the current F189 <c>ControlTransactionPayload</c> envelope
    /// (<c>roster.validators.validators[]</c>) and the pre-#1378 flat shape
    /// (<c>validators.validators[]</c>), because a ceremony output predating that change is a
    /// legitimate thing to meet. Anything else <b>throws</b> — a genesis whose shape this test does
    /// not recognise is exactly the condition it exists to report, and returning null here is what
    /// let the format move underneath it unnoticed for months.
    /// </remarks>
    private static string ReadRosterDocketSigningPubKey(string genesisJson)
    {
        using var genDoc = JsonDocument.Parse(genesisJson);
        var payloadB64 = genDoc.RootElement.GetProperty("genesisTransaction").GetProperty("payload").GetString()!;
        using var ctrlDoc = JsonDocument.Parse(Convert.FromBase64String(payloadB64));
        var root = ctrlDoc.RootElement;

        // Current: { version, roster: { ..., validators: { validators: [ ... ] } } }
        if (root.TryGetProperty("roster", out var roster)
            && roster.TryGetProperty("validators", out var rosterValidators)
            && TryReadFirstValidatorPubKey(rosterValidators, out var current))
        {
            return current;
        }

        // Pre-#1378: { ..., validators: { validators: [ ... ] } }
        if (root.TryGetProperty("validators", out var flatValidators)
            && TryReadFirstValidatorPubKey(flatValidators, out var legacy))
        {
            return legacy;
        }

        var seen = string.Join(", ", root.EnumerateObject().Select(p => p.Name));
        throw new InvalidOperationException(
            "Could not locate the validator roster in the genesis control payload. Expected either " +
            "'roster.validators.validators[0].publicKey' (current) or 'validators.validators[0].publicKey' " +
            $"(pre-#1378); the payload's top-level properties are: [{seen}]. The genesis format has " +
            "changed underneath this proof test — update the reader rather than letting it skip.");
    }

    private static bool TryReadFirstValidatorPubKey(JsonElement validatorsNode, out string publicKey)
    {
        publicKey = string.Empty;
        if (!validatorsNode.TryGetProperty("validators", out var list)
            || list.ValueKind != JsonValueKind.Array
            || list.GetArrayLength() == 0)
        {
            return false;
        }

        if (!list[0].TryGetProperty("publicKey", out var pk) || pk.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        publicKey = pk.GetString()!;
        return !string.IsNullOrEmpty(publicKey);
    }

    /// <summary>
    /// Locates the genesis validator key, returning it with the repo root it was found under.
    ///
    /// <para>Checks <c>temp/</c> at each ancestor as well as the ancestor itself. The ceremony writes
    /// the key into the gitignored <c>/temp</c> rather than the repo root (it is private key material;
    /// the old default left it one <c>git add -A</c> from being published). The repo-root location is
    /// still accepted so a pre-existing key from an earlier ceremony keeps working.</para>
    ///
    /// <para>Returning the repo root explicitly matters: this used to derive it as the key file's own
    /// directory, which was right only while the key sat AT the root.</para>
    /// </summary>
    private static (string KeyPath, string RepoRoot)? FindGenesisKeyFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var inTemp = Path.Combine(dir.FullName, "temp", "genesis-validator-key.json");
            if (File.Exists(inTemp)) return (inTemp, dir.FullName);

            var atRoot = Path.Combine(dir.FullName, "genesis-validator-key.json");
            if (File.Exists(atRoot)) return (atRoot, dir.FullName);
        }
        return null;
    }
}
