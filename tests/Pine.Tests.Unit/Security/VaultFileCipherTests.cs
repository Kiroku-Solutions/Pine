using System;
using FluentAssertions;
using Pine.Core.Security;
using Pine.Infrastructure.Security;
using Xunit;

namespace Pine.Tests.Unit.Security;

/// <summary>
/// Covers <see cref="AesGcmVaultFileCipher"/>, the TD-16 boundary that standalone vault files cross
/// on their way to disk. The categoriser's model was the first file to cross it, and Phase 3's
/// receipt attachments will be the second, so what is asserted here is a FORMAT and a set of
/// refusals, not one caller's happy path.
/// </summary>
/// <remarks>
/// <para>
/// THE NEGATIVE TESTS ARE THE POINT. A round-trip test proves only that the class can talk to
/// itself; it would pass just as happily if the "ciphertext" were the plaintext with the magic
/// stapled on the front. The tests that carry weight are the ones that must FAIL to open: a flipped
/// bit anywhere in the blob, a foreign vault's key, a different purpose, a corrupted magic. Each of
/// those is a way a stolen or damaged file could otherwise be made to yield something, and each one
/// must arrive as the same <see cref="VaultFileCipherException"/> -- the interface's own docs
/// explain why distinguishing them would answer the one question an attacker actually has.
/// </para>
/// <para>
/// THE BLOB LAYOUT IS ASSERTED LITERALLY, with the offsets spelled out as numbers rather than
/// recomputed from the production constants. Those constants are private, but more importantly a
/// test that derived the layout from the implementation could not notice the implementation
/// changing it. <c>PINEFC1\0</c> (8) || nonce (12) || tag (16) || ciphertext is an ON-DISK format;
/// a change to it is a change every previously sealed file has to survive, so it is written out
/// here as the fixed fact it is.
/// </para>
/// <para>
/// A HAND-WRITTEN STUB RATHER THAN A SUBSTITUTE for the accessor. The vault's lock state has to flip
/// mid-test and its key has to be swapped for a second vault's key, and a three-property stub says
/// that more plainly than configuring a mock's property getters would.
/// </para>
/// </remarks>
public class VaultFileCipherTests
{
    /// <summary>Offset of the nonce inside the blob. Part of the on-disk format, so written literally.</summary>
    private const int NonceOffset = 8;

    /// <summary>Offset of the authentication tag inside the blob.</summary>
    private const int TagOffset = 20;

    /// <summary>Offset at which the ciphertext begins: 8 magic + 12 nonce + 16 tag.</summary>
    private const int CiphertextOffset = 36;

    /// <summary>A second purpose, standing in for a future <c>attachments.receipt</c>.</summary>
    private const string OtherPurpose = "test.other.purpose";

    /// <summary>
    /// The vault session as this class sees it: a key, a directory, and a lock bit that a test can
    /// flip after a blob has already been sealed.
    /// </summary>
    private sealed class StubVaultAccessor : IVaultDataKeyAccessor
    {
        public bool IsUnlocked { get; set; } = true;

        public byte[]? Key { get; set; }

        public string? DbPath { get; set; } = "C:\\fake\\vault";
    }

    /// <summary>A fixed, non-degenerate 32-byte key. Deterministic so a failure is reproducible.</summary>
    private static byte[] KeyA()
    {
        var key = new byte[32];
        for (var i = 0; i < key.Length; i++)
        {
            key[i] = (byte)(i + 1);
        }

        return key;
    }

    /// <summary>A DIFFERENT 32-byte key, standing in for a second vault on the same machine.</summary>
    private static byte[] KeyB()
    {
        var key = new byte[32];
        for (var i = 0; i < key.Length; i++)
        {
            key[i] = (byte)(0xFF - i);
        }

        return key;
    }

    private static (AesGcmVaultFileCipher Cipher, StubVaultAccessor Vault) CreateCipher(byte[]? key = null)
    {
        var vault = new StubVaultAccessor { Key = key ?? KeyA() };

        return (new AesGcmVaultFileCipher(vault), vault);
    }

    /// <summary>
    /// Contiguous-subsequence search, written out rather than pulled from a helper library because
    /// the assertion it backs -- "the plaintext does not appear in the blob" -- is only meaningful if
    /// the search is exhaustive and obviously so.
    /// </summary>
    private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
        {
            return false;
        }

        for (var start = 0; start <= haystack.Length - needle.Length; start++)
        {
            var matched = true;
            for (var offset = 0; offset < needle.Length; offset++)
            {
                if (haystack[start + offset] != needle[offset])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void EncryptThenDecrypt_ShouldReturnTheOriginalBytes_UnderTheSamePurpose()
    {
        var (cipher, _) = CreateCipher();
        var plaintext = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 255, 128 };

        var blob = cipher.Encrypt(plaintext, VaultFilePurposes.CategorizerModel);
        var opened = cipher.Decrypt(blob, VaultFilePurposes.CategorizerModel);

        opened.Should().Equal(plaintext);
    }

    /// <summary>
    /// An empty model is a legitimate input the interface explicitly allows ("May be empty"), and it
    /// is the input most likely to fall out of a length calculation somewhere: a blob with a
    /// zero-length ciphertext region is exactly <see cref="CiphertextOffset"/> bytes long, which is
    /// also the shortest length the reader accepts. The boundary is asserted, not assumed.
    /// </summary>
    [Fact]
    public void EncryptThenDecrypt_ShouldRoundTripEmptyPlaintext()
    {
        var (cipher, _) = CreateCipher();

        var blob = cipher.Encrypt(ReadOnlySpan<byte>.Empty, VaultFilePurposes.CategorizerModel);
        var opened = cipher.Decrypt(blob, VaultFilePurposes.CategorizerModel);

        blob.Should().HaveCount(CiphertextOffset);
        opened.Should().BeEmpty();
    }

    /// <summary>
    /// The test that would catch the worst possible bug in this class: a "cipher" that stores the
    /// plaintext. A distinctive pattern is sealed and the whole blob is searched for it byte by
    /// byte, so the assertion fails whether the plaintext were copied into the ciphertext region,
    /// the tag region, or appended anywhere else.
    /// </summary>
    [Fact]
    public void Encrypt_ShouldNotLeaveThePlaintextAnywhereInTheBlob()
    {
        var (cipher, _) = CreateCipher();
        var plaintext = "PAYEE=Sainsburys;AMOUNT=42.17;CATEGORY=Groceries"u8.ToArray();

        var blob = cipher.Encrypt(plaintext, VaultFilePurposes.CategorizerModel);

        ContainsSubsequence(blob, plaintext).Should()
            .BeFalse("a sealed blob that contains its own plaintext is not encrypted at all");
    }

    /// <summary>
    /// A GCM nonce reused under one key is a catastrophic failure rather than a degraded one -- it
    /// leaks the authentication subkey, not merely one plaintext -- so "two seals of the same bytes
    /// differ" is not a stylistic preference, it is the property the whole construction rests on.
    /// Both the whole blob and the nonce region specifically are compared: the blob differing could
    /// in principle come from anywhere, whereas the nonce region differing is the actual claim.
    /// </summary>
    [Fact]
    public void Encrypt_ShouldUseAFreshNonceEveryCall_ForIdenticalPlaintext()
    {
        var (cipher, _) = CreateCipher();
        var plaintext = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 };

        var first = cipher.Encrypt(plaintext, VaultFilePurposes.CategorizerModel);
        var second = cipher.Encrypt(plaintext, VaultFilePurposes.CategorizerModel);

        second.Should().NotEqual(first);

        // The nonce must come fresh from the CSPRNG on every call, never from a counter: a counter
        // would have to be persisted to stay unique across restarts, and a counter that resets is
        // precisely the reuse this construction is arranged to avoid.
        second[NonceOffset..TagOffset].Should().NotEqual(first[NonceOffset..TagOffset]);
    }

    /// <summary>
    /// PURPOSE SEPARATION IS PART OF THE CONTRACT, and the interface says so in those words. The
    /// purpose selects the derived key AND is bound into the associated data, so a blob sealed for
    /// the categoriser must not open as a receipt attachment even inside the same vault.
    /// </summary>
    [Fact]
    public void Decrypt_ShouldThrow_WhenThePurposeDiffersFromTheOneSealedUnder()
    {
        var (cipher, _) = CreateCipher();
        var blob = cipher.Encrypt(new byte[] { 4, 5, 6 }, VaultFilePurposes.CategorizerModel);

        var act = () => cipher.Decrypt(blob, OtherPurpose);

        act.Should().Throw<VaultFileCipherException>();
    }

    [Fact]
    public void Decrypt_ShouldThrow_WhenABitIsFlippedInTheCiphertext()
    {
        var (cipher, _) = CreateCipher();
        var blob = cipher.Encrypt(new byte[] { 10, 20, 30, 40 }, VaultFilePurposes.CategorizerModel);

        blob[CiphertextOffset] ^= 0x01;

        var act = () => cipher.Decrypt(blob, VaultFilePurposes.CategorizerModel);

        act.Should().Throw<VaultFileCipherException>(
            "AEAD exists so a single altered byte surfaces here, not as a corrupt model three layers up");
    }

    [Fact]
    public void Decrypt_ShouldThrow_WhenABitIsFlippedInTheTag()
    {
        var (cipher, _) = CreateCipher();
        var blob = cipher.Encrypt(new byte[] { 10, 20, 30, 40 }, VaultFilePurposes.CategorizerModel);

        blob[TagOffset] ^= 0x80;

        var act = () => cipher.Decrypt(blob, VaultFilePurposes.CategorizerModel);

        act.Should().Throw<VaultFileCipherException>();
    }

    /// <summary>
    /// The nonce is neither secret nor authenticated on its own, but it IS an input to the tag
    /// computation, so altering it must still fail the open. If it did not, one blob's ciphertext
    /// could be replayed under another blob's nonce.
    /// </summary>
    [Fact]
    public void Decrypt_ShouldThrow_WhenABitIsFlippedInTheNonce()
    {
        var (cipher, _) = CreateCipher();
        var blob = cipher.Encrypt(new byte[] { 10, 20, 30, 40 }, VaultFilePurposes.CategorizerModel);

        blob[NonceOffset] ^= 0x01;

        var act = () => cipher.Decrypt(blob, VaultFilePurposes.CategorizerModel);

        act.Should().Throw<VaultFileCipherException>();
    }

    /// <summary>
    /// The magic carries the format version in its seventh character, so this is also the test that
    /// a future v2 blob is REJECTED by a v1 reader rather than misread by one.
    /// </summary>
    [Theory]
    [InlineData(0)] // 'P'
    [InlineData(6)] // the version digit
    [InlineData(7)] // the trailing NUL
    public void Decrypt_ShouldThrow_WhenTheMagicIsCorrupted(int magicByteIndex)
    {
        var (cipher, _) = CreateCipher();
        var blob = cipher.Encrypt(new byte[] { 1, 2, 3 }, VaultFilePurposes.CategorizerModel);

        blob[magicByteIndex] ^= 0xFF;

        var act = () => cipher.Decrypt(blob, VaultFilePurposes.CategorizerModel);

        act.Should().Throw<VaultFileCipherException>();
    }

    /// <summary>
    /// A blob too short to contain a header must be refused by the length check BEFORE anything
    /// slices into it -- the alternative is an <see cref="ArgumentOutOfRangeException"/> escaping a
    /// method documented to raise exactly one exception type. Zero and "one byte short of a complete
    /// header" are the two edges that matter.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]  // shorter than the magic alone
    [InlineData(8)]  // magic only
    [InlineData(35)] // one byte short of a complete header
    public void Decrypt_ShouldThrow_WhenTheBlobIsShorterThanTheHeader(int blobLength)
    {
        var (cipher, _) = CreateCipher();

        var act = () => cipher.Decrypt(new byte[blobLength], VaultFilePurposes.CategorizerModel);

        act.Should().Throw<VaultFileCipherException>();
    }

    /// <summary>
    /// The third of the three defects TD-16 exists to close: a model file copied from one vault into
    /// another must not open there. Nothing in the blob names a vault -- the vault id is deliberately
    /// NOT bound -- because the derived key already comes from that vault's own data key, so the
    /// failure lands at the tag. This test is what proves that reasoning holds in practice.
    /// </summary>
    [Fact]
    public void Decrypt_ShouldThrow_WhenTheBlobWasSealedByADifferentVaultsKey()
    {
        var (cipherA, _) = CreateCipher(KeyA());
        var (cipherB, _) = CreateCipher(KeyB());

        var blob = cipherA.Encrypt(
            "a model trained on someone else's ledger"u8.ToArray(),
            VaultFilePurposes.CategorizerModel);

        var act = () => cipherB.Decrypt(blob, VaultFilePurposes.CategorizerModel);

        act.Should().Throw<VaultFileCipherException>();
    }

    /// <summary>
    /// A DISTINCT TYPE, NOT <c>InvalidOperationException</c>, and TD-23 is the reason: the background
    /// training service has to be able to treat "the user locked up and went home" as the ordinary
    /// condition it is, by CATCHING A TYPE rather than sniffing a message for the substring "Vault".
    /// </summary>
    [Fact]
    public void Encrypt_ShouldThrowVaultLocked_WhenTheVaultIsLocked()
    {
        var (cipher, vault) = CreateCipher();
        vault.IsUnlocked = false;

        var act = () => cipher.Encrypt(new byte[] { 1, 2, 3 }, VaultFilePurposes.CategorizerModel);

        act.Should().Throw<VaultLockedException>();
    }

    [Fact]
    public void Decrypt_ShouldThrowVaultLocked_WhenTheVaultIsLocked()
    {
        var (cipher, vault) = CreateCipher();
        var blob = cipher.Encrypt(new byte[] { 1, 2, 3 }, VaultFilePurposes.CategorizerModel);

        // Locked AFTER the blob was sealed, which is the real sequence: the auto-lock timer fires
        // while a background service still holds a file it meant to open.
        vault.IsUnlocked = false;

        var act = () => cipher.Decrypt(blob, VaultFilePurposes.CategorizerModel);

        act.Should().Throw<VaultLockedException>();
    }

    /// <summary>
    /// An empty purpose would derive a key from the HKDF info prefix alone -- one "default" key
    /// shared by every caller who forgot to name a purpose, which is exactly the separation this
    /// class exists to provide. Whitespace is rejected alongside null and empty because the purpose
    /// goes into the derivation verbatim, so " " and "  " would silently be two different keys.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Encrypt_ShouldThrowArgumentException_ForAMissingPurpose(string? purpose)
    {
        var (cipher, _) = CreateCipher();

        var act = () => cipher.Encrypt(new byte[] { 1, 2, 3 }, purpose!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Decrypt_ShouldThrowArgumentException_ForAMissingPurpose(string? purpose)
    {
        var (cipher, _) = CreateCipher();
        var blob = cipher.Encrypt(new byte[] { 1, 2, 3 }, VaultFilePurposes.CategorizerModel);

        var act = () => cipher.Decrypt(blob, purpose!);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The layout, asserted as the on-disk format it is. AES-GCM's ciphertext is the same length as
    /// its plaintext, so a blob is exactly the header plus whatever was sealed -- and that identity
    /// is what lets the reader recover the plaintext length by subtraction instead of storing it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(4096)]
    public void Encrypt_ShouldProduceABlobOfTheDocumentedLength(int plaintextLength)
    {
        var (cipher, _) = CreateCipher();

        var blob = cipher.Encrypt(new byte[plaintextLength], VaultFilePurposes.CategorizerModel);

        blob.Should().HaveCount(8 + 12 + 16 + plaintextLength);
    }

    /// <summary>The magic is the first thing any reader -- including a forensic one -- sees.</summary>
    [Fact]
    public void Encrypt_ShouldStampTheDocumentedMagicAtTheStartOfTheBlob()
    {
        var (cipher, _) = CreateCipher();

        var blob = cipher.Encrypt(new byte[] { 1 }, VaultFilePurposes.CategorizerModel);

        // Spelled out byte by byte rather than as a string literal, because the trailing NUL is
        // part of the magic and a string literal is exactly where a trailing NUL goes missing.
        var expectedMagic = new byte[]
        {
            (byte)'P', (byte)'I', (byte)'N', (byte)'E', (byte)'F', (byte)'C', (byte)'1', 0,
        };

        blob[..NonceOffset].Should().Equal(expectedMagic);
    }
}
