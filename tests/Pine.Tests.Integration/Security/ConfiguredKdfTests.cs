using System.IO;
using FluentAssertions;
using Pine.Core.Configuration;
using Pine.Core.Security;
using Pine.Infrastructure.Security;

namespace Pine.Tests.Integration.Security;

/// <summary>
/// The configured Argon2id profile reaches new vaults, and reaches nothing else (TD-27).
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE TEST THAT MAKES THE KDF SAFE TO CONFIGURE AT ALL. A vault's parameters are written
/// into its own <c>pine.vault.json</c> at creation and read back from there on every unlock. If a
/// configured cost were ever consulted at unlock instead, changing one number in a file would
/// derive a different auth key for every vault already on disk, and the symptom — an
/// <c>VaultKeyUnwrapException</c> — is indistinguishable from a wrong password. Not "hard to
/// diagnose": indistinguishable, by design, because the unlock screen must not become an oracle.
/// </para>
/// <para>
/// REAL ARGON2ID AND REAL FILES. The whole claim is about what is on disk and what is derived from
/// it, so substituting either half would test nothing. The profiles below are deliberately cheap —
/// the production cost is pinned by the known-answer vector in
/// <c>Argon2idKeyDerivationServiceTests</c>, and these are about which parameters are USED, not
/// about what they cost.
/// </para>
/// </remarks>
public class ConfiguredKdfTests
{
    private const string Password = "correct horse battery staple";

    /// <summary>Calls the production conversion, rather than restating it.</summary>
    /// <remarks>
    /// <c>PineSecurityOptions.ToKdfParameters()</c> is the one bridge from configuration to
    /// cryptography and <c>SetupViewModel</c> is its only caller. This test originally reimplemented
    /// that mapping, which would have let a change to the real one pass unnoticed — a test that
    /// restates the logic it checks proves only that the author can write it twice.
    /// </remarks>
    private static KdfParameters ProfileFrom(PineSecurityOptions options) => options.ToKdfParameters();

    [Fact]
    public void AConfiguredProfile_ShouldBeTheOneWrittenIntoTheNewVaultsHeader()
    {
        var configured = new PineSecurityOptions
        {
            NewVaultKdfMemoryKib = 16,
            NewVaultKdfIterations = 2,
            NewVaultKdfParallelism = 1,
        };

        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());

        var material = service.CreateVault(Password, ProfileFrom(configured));

        material.Header.KdfParameters.MemoryKib.Should().Be(16);
        material.Header.KdfParameters.Iterations.Should().Be(2);
        material.Header.KdfParameters.Parallelism.Should().Be(1);

        // Not configurable, and asserted so that making them configurable is a visible decision.
        material.Header.KdfParameters.KeyLengthBytes.Should().Be(32);
        material.Header.KdfParameters.SaltLengthBytes.Should().Be(KdfParameters.Argon2idV1.SaltLengthBytes);
    }

    /// <summary>
    /// A vault created under one configured profile still unlocks after the configuration changes.
    /// </summary>
    /// <remarks>
    /// THE ONE THAT MATTERS. It writes a real header to a real directory under profile A, then
    /// changes the configuration to profile B — a different memory cost AND a different iteration
    /// count — and unlocks the vault. It succeeds because nothing on the unlock path reads
    /// configuration: the header is the only source of the parameters. Were that ever to change,
    /// this test would fail with the same exception a wrong password produces, which is exactly the
    /// failure mode being prevented.
    /// </remarks>
    [Fact]
    public void AVaultCreatedUnderOneProfile_ShouldStillUnlockAfterTheConfigurationChanges()
    {
        using var directory = new VaultTestDirectory();

        var store = new FileVaultHeaderStore();
        var service = new AesGcmVaultKeyService(new Argon2idKeyDerivationService());

        var atCreation = new PineSecurityOptions
        {
            NewVaultKdfMemoryKib = 16,
            NewVaultKdfIterations = 1,
            NewVaultKdfParallelism = 1,
        };

        var material = service.CreateVault(Password, ProfileFrom(atCreation));
        store.Write(directory.Path, material.Header);

        // The operator edits appsettings.json. Every value differs from the one the vault was
        // minted with, so a derivation that consulted configuration would produce a different auth
        // key and the unwrap below would throw.
        var afterEditing = new PineSecurityOptions
        {
            NewVaultKdfMemoryKib = 64,
            NewVaultKdfIterations = 3,
            NewVaultKdfParallelism = 2,
        };

        ProfileFrom(afterEditing).Should().NotBe(ProfileFrom(atCreation),
            "this test is vacuous if the two profiles are the same");

        var readBack = store.Read(directory.Path);
        readBack!.KdfParameters.MemoryKib.Should().Be(16, "the header records what the vault was minted with");

        var unwrapped = service.UnwrapDataKey(Password, readBack);

        unwrapped.Should().Equal(
            material.DataKey,
            "a vault's parameters live in its own header; changing configuration must never lock an existing vault out");
    }

    /// <summary>
    /// The shipped defaults still mint exactly the profile every existing vault was created with.
    /// </summary>
    /// <remarks>
    /// A configuration system's first duty is to change nothing. If this drifts, every vault
    /// created after the change gets a different profile than the documented one — legal, but it
    /// must be deliberate.
    /// </remarks>
    [Fact]
    public void TheDefaultConfiguration_ShouldMintTheDocumentedProfile()
    {
        ProfileFrom(new PineSecurityOptions()).Should().Be(KdfParameters.Argon2idV1);
    }
}
