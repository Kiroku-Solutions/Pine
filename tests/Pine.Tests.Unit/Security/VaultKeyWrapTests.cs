using System;
using FluentAssertions;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Unit.Security;

/// <summary>
/// Covers the envelope's payload type (TD-06). A <see cref="VaultKeyWrap"/> is the only form in
/// which a vault's data key touches disk, so the invariants asserted here are the ones standing
/// between a wrong-sized nonce and a silently weakened vault.
/// </summary>
public class VaultKeyWrapTests
{
    private static byte[] Nonce() => new byte[VaultKeyWrap.Aes256GcmNonceLengthBytes];

    private static byte[] Tag() => new byte[VaultKeyWrap.Aes256GcmTagLengthBytes];

    private static byte[] Ciphertext() => new byte[32];

    [Fact]
    public void Constants_ShouldMatchTheAesGcmProfile()
    {
        // A 96-bit nonce and a full 128-bit tag are the only sizes NIST SP 800-38D recommends
        // without caveats. If either constant ever drifts, every wrap written afterwards is a
        // different format, so they are asserted rather than assumed.
        VaultKeyWrap.Aes256GcmNonceLengthBytes.Should().Be(12);
        VaultKeyWrap.Aes256GcmTagLengthBytes.Should().Be(16);
        VaultKeyWrapAlgorithms.Aes256Gcm.Should().Be("AES-256-GCM");
    }

    [Fact]
    public void Constructor_ShouldExposeEveryValueItWasGiven()
    {
        // Arrange
        var nonce = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        var ciphertext = new byte[] { 20, 21, 22 };
        var tag = new byte[] { 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45 };

        // Act
        var wrap = new VaultKeyWrap(VaultKeyWrapAlgorithms.Aes256Gcm, nonce, ciphertext, tag);

        // Assert
        wrap.Algorithm.Should().Be(VaultKeyWrapAlgorithms.Aes256Gcm);
        wrap.Nonce.Should().Equal(nonce);
        wrap.Ciphertext.Should().Equal(ciphertext);
        wrap.Tag.Should().Equal(tag);
        wrap.CiphertextLength.Should().Be(ciphertext.Length);
    }

    [Fact]
    public void Arrays_ShouldBeDefensiveCopies_InBothDirections()
    {
        // Arrange
        var nonce = Nonce();
        var ciphertext = Ciphertext();
        var tag = Tag();
        var wrap = new VaultKeyWrap(VaultKeyWrapAlgorithms.Aes256Gcm, nonce, ciphertext, tag);

        // Act - mutate the caller's arrays, and the arrays handed back out
        nonce[0] = 0xFF;
        ciphertext[0] = 0xFF;
        tag[0] = 0xFF;
        wrap.Nonce[1] = 0xEE;
        wrap.Ciphertext[1] = 0xEE;
        wrap.Tag[1] = 0xEE;

        // Assert - a constructed wrap is a fact about a vault; nobody may edit it after the fact
        wrap.Nonce[0].Should().Be(0);
        wrap.Ciphertext[0].Should().Be(0);
        wrap.Tag[0].Should().Be(0);
        wrap.Nonce[1].Should().Be(0);
        wrap.Ciphertext[1].Should().Be(0);
        wrap.Tag[1].Should().Be(0);
    }

    [Fact]
    public void Constructor_ShouldRejectAnUnknownAlgorithm()
    {
        // Refused at construction, not at decryption: no code path anywhere should be able to hold
        // a wrap whose algorithm nobody implements.
        var act = () => new VaultKeyWrap("AES-128-CBC", Nonce(), Ciphertext(), Tag());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldRejectTheAlgorithmNameInTheWrongCase()
    {
        // Ordinal comparison, deliberately. The algorithm string is written by Pine and read back
        // by Pine; accepting variants would mean the header has more than one spelling of the same
        // fact, and the binding in VaultHeaderBinding authenticates the exact bytes.
        var act = () => new VaultKeyWrap("aes-256-gcm", Nonce(), Ciphertext(), Tag());

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]  // absent
    [InlineData(11)] // one short
    [InlineData(13)] // one long
    [InlineData(16)] // the TAG length, a plausible copy-paste error
    public void Constructor_ShouldRejectANonceThatIsNotTwelveBytes(int nonceLength)
    {
        var act = () => new VaultKeyWrap(
            VaultKeyWrapAlgorithms.Aes256Gcm,
            new byte[nonceLength],
            Ciphertext(),
            Tag());

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]  // absent
    [InlineData(12)] // the NONCE length, the mirror-image copy-paste error
    [InlineData(15)] // truncated by one - a weakened tag, not a broken one, which is the dangerous case
    [InlineData(17)]
    public void Constructor_ShouldRejectATagThatIsNotSixteenBytes(int tagLength)
    {
        var act = () => new VaultKeyWrap(
            VaultKeyWrapAlgorithms.Aes256Gcm,
            Nonce(),
            Ciphertext(),
            new byte[tagLength]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldRejectAnEmptyCiphertext()
    {
        // An empty ciphertext is a wrap of nothing. It would round-trip through the header file
        // perfectly and produce a zero-length data key, which SQLCipher would reject with a
        // message about the database rather than about the key.
        var act = () => new VaultKeyWrap(VaultKeyWrapAlgorithms.Aes256Gcm, Nonce(), [], Tag());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldRejectNullArguments()
    {
        var nullAlgorithm = () => new VaultKeyWrap(null!, Nonce(), Ciphertext(), Tag());
        var nullNonce = () => new VaultKeyWrap(VaultKeyWrapAlgorithms.Aes256Gcm, null!, Ciphertext(), Tag());
        var nullCiphertext = () => new VaultKeyWrap(VaultKeyWrapAlgorithms.Aes256Gcm, Nonce(), null!, Tag());
        var nullTag = () => new VaultKeyWrap(VaultKeyWrapAlgorithms.Aes256Gcm, Nonce(), Ciphertext(), null!);

        nullAlgorithm.Should().Throw<ArgumentNullException>();
        nullNonce.Should().Throw<ArgumentNullException>();
        nullCiphertext.Should().Throw<ArgumentNullException>();
        nullTag.Should().Throw<ArgumentNullException>();
    }
}
