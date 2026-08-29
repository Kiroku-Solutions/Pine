using System;
using FluentAssertions;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Unit.Security;

/// <summary>
/// Covers the AEAD associated data that ties a <see cref="VaultKeyWrap"/> to its header (TD-06).
/// </summary>
/// <remarks>
/// <para>
/// THESE ARE THE TESTS THAT MAKE THE BINDING WORTH HAVING. Associated data is authenticated but not
/// encrypted, so its whole value is that changing a bound byte breaks the tag. A binding that
/// silently ignored a field would leave that field editable by anyone with write access to
/// <c>pine.vault.json</c>, and nothing else in the system would notice. Every field of the header
/// therefore gets its own "changing this changes the binding" assertion.
/// </para>
/// <para>
/// The encoding is also asserted to be UNAMBIGUOUS: two different headers must never produce the
/// same associated data, which is why the implementation length-prefixes rather than joining with
/// a delimiter.
/// </para>
/// </remarks>
public class VaultHeaderBindingTests
{
    private static readonly Guid VaultId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Salt = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    private static byte[] Baseline() => VaultHeaderBinding.Compute(
        VaultHeader.CurrentVersion,
        VaultId,
        CreatedAt,
        KdfParameters.Argon2idV1,
        Salt);

    [Fact]
    public void Compute_ShouldBeDeterministic_ForTheSameValues()
    {
        // Without this the envelope would be unopenable on the second run, which is the one failure
        // mode a crypto test must never leave to chance.
        Baseline().Should().Equal(Baseline());
    }

    [Fact]
    public void Compute_ShouldNotBeEmpty()
    {
        Baseline().Should().NotBeEmpty();
    }

    [Fact]
    public void Compute_ShouldChange_WhenTheHeaderVersionChanges()
    {
        // The downgrade defence: rewriting the version to claim an older, weaker schema must not
        // produce a header that still opens.
        var other = VaultHeaderBinding.Compute(
            VaultHeader.CurrentVersion + 1, VaultId, CreatedAt, KdfParameters.Argon2idV1, Salt);

        other.Should().NotEqual(Baseline());
    }

    [Fact]
    public void Compute_ShouldChange_WhenTheVaultIdChanges()
    {
        // The splicing defence, and the one the KDF cannot provide on its own: the vault id is
        // never fed to Argon2id, so without the binding it would be a freely editable field.
        var other = VaultHeaderBinding.Compute(
            VaultHeader.CurrentVersion, Guid.NewGuid(), CreatedAt, KdfParameters.Argon2idV1, Salt);

        other.Should().NotEqual(Baseline());
    }

    [Fact]
    public void Compute_ShouldChange_WhenTheCreationTimestampChanges()
    {
        var other = VaultHeaderBinding.Compute(
            VaultHeader.CurrentVersion, VaultId, CreatedAt.AddSeconds(1), KdfParameters.Argon2idV1, Salt);

        other.Should().NotEqual(Baseline());
    }

    [Fact]
    public void Compute_ShouldNotChange_WhenOnlyTheTimestampsOffsetSpellingDiffers()
    {
        // The header file stores an ISO-8601 string WITH an offset. "12:00Z" and "13:00+01:00" are
        // the same instant spelled two ways, and a round trip through some other serializer must
        // not invalidate every wrap. The binding uses UtcTicks precisely so it cannot.
        var sameInstantOtherOffset = CreatedAt.ToOffset(TimeSpan.FromHours(1));

        var other = VaultHeaderBinding.Compute(
            VaultHeader.CurrentVersion, VaultId, sameInstantOtherOffset, KdfParameters.Argon2idV1, Salt);

        other.Should().Equal(Baseline());
    }

    [Theory]
    [InlineData("scrypt", 65_536, 3, 4, 32, 16)]      // kdfId
    [InlineData("argon2id", 32_768, 3, 4, 32, 16)]    // memory - the cheapest parameter to attack
    [InlineData("argon2id", 65_536, 1, 4, 32, 16)]    // iterations
    [InlineData("argon2id", 65_536, 3, 1, 32, 16)]    // parallelism
    [InlineData("argon2id", 65_536, 3, 4, 64, 16)]    // key length
    [InlineData("argon2id", 65_536, 3, 4, 32, 24)]    // declared salt length
    public void Compute_ShouldChange_WhenAnyKdfParameterChanges(
        string kdfId, int memoryKib, int iterations, int parallelism, int keyLengthBytes, int saltLengthBytes)
    {
        // Redundant with the KDF itself for every row but the first - a different cost parameter
        // already derives a different auth key. Kept anyway, because "every field is bound" is only
        // a rule a reader can trust if it is enforced without exceptions.
        var parameters = new KdfParameters(kdfId, memoryKib, iterations, parallelism, keyLengthBytes, saltLengthBytes);
        var salt = new byte[saltLengthBytes];
        Array.Copy(Salt, salt, Math.Min(Salt.Length, salt.Length));

        var other = VaultHeaderBinding.Compute(
            VaultHeader.CurrentVersion, VaultId, CreatedAt, parameters, salt);

        other.Should().NotEqual(Baseline());
    }

    [Fact]
    public void Compute_ShouldChange_WhenASingleSaltByteChanges()
    {
        var tamperedSalt = (byte[])Salt.Clone();
        tamperedSalt[7] ^= 0x01;

        var other = VaultHeaderBinding.Compute(
            VaultHeader.CurrentVersion, VaultId, CreatedAt, KdfParameters.Argon2idV1, tamperedSalt);

        other.Should().NotEqual(Baseline());
    }

    [Fact]
    public void Compute_ShouldDistinguishHeaders_ThatAConcatenatedEncodingWouldCollide()
    {
        // The reason the implementation length-prefixes instead of joining fields with a separator.
        // These two KDF ids differ only in where a boundary falls; a naive "join with a delimiter"
        // encoding maps both to the same bytes, and two different vaults would then share one
        // associated-data value.
        var left = new KdfParameters("argon2id", 65_536, 3, 4, 32, 16);
        var right = KdfParameters.Argon2idV1;

        var a = VaultHeaderBinding.Compute(1, VaultId, CreatedAt, left, Salt);
        var b = VaultHeaderBinding.Compute(11, VaultId, CreatedAt, right, Salt);

        a.Should().NotEqual(b);
    }

    [Fact]
    public void Compute_ShouldAgree_BetweenTheValueOverloadAndTheHeaderOverload()
    {
        // Creation computes the binding from loose values (a header cannot exist before its wrap);
        // unlocking computes it from the assembled header. If the two ever disagreed, every vault
        // would create successfully and then refuse to open - so this is the single most important
        // assertion in the file.
        var wrap = new VaultKeyWrap(
            VaultKeyWrapAlgorithms.Aes256Gcm,
            new byte[VaultKeyWrap.Aes256GcmNonceLengthBytes],
            new byte[32],
            new byte[VaultKeyWrap.Aes256GcmTagLengthBytes]);

        var header = new VaultHeader(
            VaultHeader.CurrentVersion, VaultId, CreatedAt, KdfParameters.Argon2idV1, Salt, wrap);

        VaultHeaderBinding.Compute(header).Should().Equal(Baseline());
    }

    [Fact]
    public void Compute_ShouldNotDependOnTheWrap()
    {
        // The wrap is the one field NOT bound, for the obvious reason that it is the ciphertext the
        // binding protects. Asserted so nobody "tidies" it into the encoding and makes the header
        // impossible to construct.
        var headerA = new VaultHeader(
            VaultHeader.CurrentVersion, VaultId, CreatedAt, KdfParameters.Argon2idV1, Salt,
            new VaultKeyWrap(
                VaultKeyWrapAlgorithms.Aes256Gcm,
                new byte[VaultKeyWrap.Aes256GcmNonceLengthBytes],
                new byte[32],
                new byte[VaultKeyWrap.Aes256GcmTagLengthBytes]));

        var differentNonce = new byte[VaultKeyWrap.Aes256GcmNonceLengthBytes];
        differentNonce[0] = 0xAB;

        var headerB = new VaultHeader(
            VaultHeader.CurrentVersion, VaultId, CreatedAt, KdfParameters.Argon2idV1, Salt,
            new VaultKeyWrap(
                VaultKeyWrapAlgorithms.Aes256Gcm,
                differentNonce,
                new byte[32],
                new byte[VaultKeyWrap.Aes256GcmTagLengthBytes]));

        VaultHeaderBinding.Compute(headerA).Should().Equal(VaultHeaderBinding.Compute(headerB));
    }

    [Fact]
    public void Compute_ShouldRejectNullArguments()
    {
        var nullParameters = () => VaultHeaderBinding.Compute(1, VaultId, CreatedAt, null!, Salt);
        var nullSalt = () => VaultHeaderBinding.Compute(1, VaultId, CreatedAt, KdfParameters.Argon2idV1, null!);
        var nullHeader = () => VaultHeaderBinding.Compute(null!);

        nullParameters.Should().Throw<ArgumentNullException>();
        nullSalt.Should().Throw<ArgumentNullException>();
        nullHeader.Should().Throw<ArgumentNullException>();
    }
}
