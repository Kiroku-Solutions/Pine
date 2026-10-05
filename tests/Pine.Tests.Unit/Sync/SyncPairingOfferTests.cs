using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Pine.Core.Sync;
using Pine.Infrastructure.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// The pairing offer: the sync key wrapped under a one-time code (post-audit plan item 3.1e; TD-154).
/// </summary>
/// <remarks>
/// EVERY REFUSAL IS EXERCISED HERE, and each one asserts that what comes back is the reason the format says it is
/// — not merely that something was thrown. The bit-flip theory is the one that proves the ordering: a change
/// anywhere in the header or the body must be <see cref="SyncPairingRefusal.NotAuthentic"/> and never "expired",
/// "same vault" or "wrong payload", because those three are only asked after the tag has spoken.
/// </remarks>
public class SyncPairingOfferTests
{
    private static readonly Guid Offering = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Accepting = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Noon = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static SyncKey Key(byte start = 1) =>
        SyncKey.FromBytes(Enumerable.Range(start, 32).Select(i => (byte)i).ToArray());

    private static string SampleCode() => SyncPairingCode.Generate();

    private static byte[] Offer(SyncKey key, string code, TimeSpan? lifetime = null, Guid? from = null) =>
        SyncPairingOffer.Create(key, from ?? Offering, code, Noon, lifetime ?? SyncPairingLimits.DefaultLifetime);

    private static SyncPairingRefusal RefusalOf(Action act) =>
        act.Should().Throw<SyncPairingRefusedException>().Which.Refusal;

    [Fact]
    public void AnOffer_HasTheDocumentedShape_AndCarriesNoPlaintextKey()
    {
        using var key = Key();
        var code = SampleCode();

        var offer = Offer(key, code);

        offer.Should().HaveCount(SyncPairingOffer.Length);
        SyncPairingOffer.Length.Should().Be(166);
        SyncPairingOffer.HeaderLength.Should().Be(118);
        Encoding.ASCII.GetString(offer, 0, 8).Should().Be("PINEPAIR");
        BinaryPrimitives.ReadUInt16LittleEndian(offer.AsSpan(8)).Should().Be(SyncPairingOffer.CurrentVersion);

        var material = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

        // Neither the key nor the code appears anywhere in the bytes that are written.
        IndexOf(offer, material).Should().Be(-1, "the key is only ever ciphertext on disk");
        IndexOf(offer, Encoding.ASCII.GetBytes(SyncPairingCode.Normalise(code)!)).Should().Be(-1);
    }

    [Fact]
    public void TheHeader_SaysWhoOfferedWhatKey_AndWhenItDies()
    {
        using var key = Key();
        var offer = Offer(key, SampleCode(), TimeSpan.FromMinutes(7));

        var header = SyncPairingOffer.ReadHeader(offer);

        header.Version.Should().Be(1);
        header.OfferingVaultId.Should().Be(Offering);
        header.OfferId.Should().NotBe(Guid.Empty);
        header.KeyId.Should().Equal(key.KeyId);
        header.CreatedAtUtc.Should().Be(Noon);
        header.ExpiresAtUtc.Should().Be(Noon.AddMinutes(7));
    }

    [Fact]
    public void TwoOffers_ShareNoSaltNonceOfferIdOrCiphertext()
    {
        using var key = Key();
        var code = SampleCode();

        var offers = Enumerable.Range(0, 200).Select(_ => Offer(key, code)).ToList();

        offers.Select(o => Convert.ToHexString(o.AsSpan(74, 32))).Distinct().Should().HaveCount(200, "the salt is fresh per offer");
        offers.Select(o => Convert.ToHexString(o.AsSpan(106, 12))).Distinct().Should().HaveCount(200, "the nonce is fresh per offer");
        offers.Select(o => Convert.ToHexString(o.AsSpan(26, 16))).Distinct().Should().HaveCount(200, "each offer is its own offer");
        offers.Select(o => Convert.ToHexString(o.AsSpan(134))).Distinct().Should().HaveCount(200,
            "a repeated ciphertext under one code would mean a repeated GCM key and nonce");
    }

    [Fact]
    public void TheRightCode_OpensIt_AndYieldsTheSameKey()
    {
        using var key = Key();
        var code = SampleCode();
        var offer = Offer(key, code);

        using var opened = SyncPairingOffer.Open(offer, code, Accepting, Noon.AddMinutes(1));

        opened.KeyId.Should().Equal(key.KeyId);
        opened.Fingerprint.Should().Be(key.Fingerprint);
        key.HasId(opened.KeyId).Should().BeTrue();
    }

    [Fact]
    public void TheCodeMayBeTypedAnyWay()
    {
        using var key = Key();
        var code = SampleCode();
        var offer = Offer(key, code);

        using var opened = SyncPairingOffer.Open(offer, code.Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant(), Accepting, Noon);

        opened.KeyId.Should().Equal(key.KeyId);
    }

    [Fact]
    public void TheFingerprint_AgreesOnBothSides_AndDiffersBetweenKeys()
    {
        using var a = Key(1);
        using var again = Key(1);
        using var b = Key(2);

        a.Fingerprint.Should().Be(again.Fingerprint).And.MatchRegex("^[0-9A-Z]{4}(-[0-9A-Z]{4}){3}$");
        a.Fingerprint.Should().NotBe(b.Fingerprint);
        a.Fingerprint.Should().NotBe(Convert.ToHexString(a.KeyId), "the fingerprint is its own derivation, not the key id");
    }

    [Fact]
    public void TheFingerprint_IsNotTheKeyIdInOtherClothes()
    {
        using var key = Key();

        // The key id travels IN THE CLEAR in every envelope header. Derived with the key id's own info, the
        // sixteen characters a person reads aloud would be a re-encoding of the first ten bytes of that public
        // routing value — and the thing people compare would then be pinned to the wire format for ever. Both
        // are HKDF outputs and neither leaks the key, so nothing else in the suite can tell the two apart.
        Grouped(key.KeyId.AsSpan(0, SyncKey.FingerprintLengthBytes)).Should().NotBe(key.Fingerprint);
    }

    /// <summary>The same grouped base32 the fingerprint is shown in, written independently of the class.</summary>
    private static string Grouped(ReadOnlySpan<byte> bytes)
    {
        var bits = 0;
        var count = 0;
        var text = new StringBuilder();
        foreach (var b in bytes)
        {
            bits = (bits << 8) | b;
            count += 8;
            while (count >= 5)
            {
                count -= 5;
                if (text.Length is 4 or 9 or 14)
                {
                    text.Append('-');
                }

                text.Append(SyncPairingCode.Alphabet[(bits >> count) & 31]);
            }
        }

        return text.ToString();
    }

    [Fact]
    public void TheFingerprint_SurvivesDisposal_BecauseItIsWhatTheUserCompares()
    {
        var key = Key();
        var fingerprint = key.Fingerprint;
        key.Dispose();

        key.Fingerprint.Should().Be(fingerprint);
    }

    [Fact]
    public void AMistypedCode_IsRefusedAsNotAuthentic()
    {
        using var key = Key();
        var offer = Offer(key, SampleCode());

        RefusalOf(() => SyncPairingOffer.Open(offer, SampleCode(), Accepting, Noon))
            .Should().Be(SyncPairingRefusal.NotAuthentic);
    }

    [Fact]
    public void SomethingThatIsNotACode_IsRefusedBeforeAnyCryptography()
    {
        using var key = Key();
        var offer = Offer(key, SampleCode());

        RefusalOf(() => SyncPairingOffer.Open(offer, "nope", Accepting, Noon)).Should().Be(SyncPairingRefusal.CodeFormat);
        RefusalOf(() => SyncPairingOffer.Open(offer, string.Empty, Accepting, Noon)).Should().Be(SyncPairingRefusal.CodeFormat);
    }

    [Fact]
    public void AnExpiredOffer_IsRefused_AtTheInstantItExpires()
    {
        using var key = Key();
        var code = SampleCode();
        var offer = Offer(key, code, TimeSpan.FromMinutes(10));

        SyncPairingOffer.Open(offer, code, Accepting, Noon.AddMinutes(10).AddMilliseconds(-1)).Dispose();
        RefusalOf(() => SyncPairingOffer.Open(offer, code, Accepting, Noon.AddMinutes(10)))
            .Should().Be(SyncPairingRefusal.Expired);
        RefusalOf(() => SyncPairingOffer.Open(offer, code, Accepting, Noon.AddDays(1)))
            .Should().Be(SyncPairingRefusal.Expired);
    }

    [Fact]
    public void ThisVaultsOwnOffer_IsRefused()
    {
        using var key = Key();
        var code = SampleCode();
        var offer = Offer(key, code);

        RefusalOf(() => SyncPairingOffer.Open(offer, code, Offering, Noon)).Should().Be(SyncPairingRefusal.SameVault);
    }

    [Fact]
    public void AnAuthenticOfferCarryingSomethingElse_IsRefusedAsAPayloadMismatch()
    {
        using var key = Key();
        var code = SampleCode();
        var header = new SyncPairingOfferHeader(1, Offering, Guid.NewGuid(), key.KeyId, Noon, Noon.AddMinutes(10));

        var lying = SyncPairingOffer.CreateCore(header, Enumerable.Range(90, 32).Select(i => (byte)i).ToArray(), code, Salt(), Nonce());
        RefusalOf(() => SyncPairingOffer.Open(lying, code, Accepting, Noon)).Should().Be(SyncPairingRefusal.PayloadMismatch);

        var cleared = SyncPairingOffer.CreateCore(header, new byte[32], code, Salt(), Nonce());
        RefusalOf(() => SyncPairingOffer.Open(cleared, code, Accepting, Noon)).Should().Be(SyncPairingRefusal.PayloadMismatch);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(165)]
    [InlineData(167)]
    public void AnOfferOfTheWrongLength_IsMalformed(int length)
    {
        RefusalOf(() => SyncPairingOffer.ReadHeader(new byte[length])).Should().Be(SyncPairingRefusal.Malformed);
    }

    [Fact]
    public void EveryTruncationAndEveryExtension_IsMalformed()
    {
        using var key = Key();
        var code = SampleCode();
        var offer = Offer(key, code);

        for (var length = 0; length < offer.Length; length++)
        {
            RefusalOf(() => SyncPairingOffer.Open(offer.AsSpan(0, length).ToArray(), code, Accepting, Noon))
                .Should().Be(SyncPairingRefusal.Malformed, $"a {length}-byte offer is not one");
        }

        RefusalOf(() => SyncPairingOffer.Open([.. offer, 0], code, Accepting, Noon)).Should().Be(SyncPairingRefusal.Malformed);
    }

    [Fact]
    public void SomethingElseEntirely_IsMalformed()
    {
        using var key = Key();
        var offer = Offer(key, SampleCode());
        var notOurs = (byte[])offer.Clone();
        notOurs[0] = (byte)'X';

        RefusalOf(() => SyncPairingOffer.ReadHeader(notOurs)).Should().Be(SyncPairingRefusal.Malformed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void AnotherVersion_IsRefusedAsTheVersion(ushort version)
    {
        using var key = Key();
        var offer = Offer(key, SampleCode());
        BinaryPrimitives.WriteUInt16LittleEndian(offer.AsSpan(8), version);

        RefusalOf(() => SyncPairingOffer.ReadHeader(offer)).Should().Be(SyncPairingRefusal.UnsupportedVersion);
    }

    [Fact]
    public void AHeaderNoOffererWrites_IsMalformed()
    {
        using var key = Key();
        var code = SampleCode();

        var noVault = Offer(key, code);
        Guid.Empty.TryWriteBytes(noVault.AsSpan(10, 16));
        RefusalOf(() => SyncPairingOffer.ReadHeader(noVault)).Should().Be(SyncPairingRefusal.Malformed);

        var noOfferId = Offer(key, code);
        Guid.Empty.TryWriteBytes(noOfferId.AsSpan(26, 16));
        RefusalOf(() => SyncPairingOffer.ReadHeader(noOfferId)).Should().Be(SyncPairingRefusal.Malformed);

        var backwards = Offer(key, code);
        BinaryPrimitives.WriteInt64LittleEndian(backwards.AsSpan(66), Noon.AddMinutes(-1).ToUnixTimeMilliseconds());
        RefusalOf(() => SyncPairingOffer.ReadHeader(backwards)).Should().Be(SyncPairingRefusal.Malformed);

        var immortal = Offer(key, code);
        BinaryPrimitives.WriteInt64LittleEndian(immortal.AsSpan(66), Noon.AddDays(30).ToUnixTimeMilliseconds());
        RefusalOf(() => SyncPairingOffer.ReadHeader(immortal)).Should().Be(SyncPairingRefusal.Malformed);

        var notATime = Offer(key, code);
        BinaryPrimitives.WriteInt64LittleEndian(notATime.AsSpan(58), long.MinValue);
        RefusalOf(() => SyncPairingOffer.ReadHeader(notATime)).Should().Be(SyncPairingRefusal.Malformed);
    }

    [Fact]
    public void EveryByte_IsBoundByTheTag_AndAnEditIsNeverReportedAsSomethingElse()
    {
        using var key = Key();
        var code = SampleCode();
        var original = Offer(key, code, TimeSpan.FromMinutes(10));

        for (var index = 0; index < original.Length; index++)
        {
            foreach (var mask in new byte[] { 0x01, 0x80 })
            {
                var edited = (byte[])original.Clone();
                edited[index] ^= mask;

                var refusal = RefusalOf(() => SyncPairingOffer.Open(edited, code, Accepting, Noon));

                // The magic, the version and the header's own consistency rules are read before anything is
                // decrypted; everything else must fail the tag, and must never be reported as expiry, as this
                // vault's own offer, or as a payload problem.
                var shapeRegion = index < 10 || IsHeaderShapeField(index);
                refusal.Should().BeOneOf(shapeRegion
                    ? new[] { SyncPairingRefusal.Malformed, SyncPairingRefusal.UnsupportedVersion, SyncPairingRefusal.NotAuthentic }
                    : new[] { SyncPairingRefusal.NotAuthentic },
                    $"byte {index} with mask {mask:X2}");
            }
        }
    }

    [Fact]
    public void AHeaderSplicedOntoAnothersBody_IsRefused()
    {
        using var key = Key();
        var code = SampleCode();
        var a = Offer(key, code);
        var b = Offer(key, code);

        var spliced = (byte[])a.Clone();
        b.AsSpan(SyncPairingOffer.HeaderLength).CopyTo(spliced.AsSpan(SyncPairingOffer.HeaderLength));

        RefusalOf(() => SyncPairingOffer.Open(spliced, code, Accepting, Noon)).Should().Be(SyncPairingRefusal.NotAuthentic);
    }

    [Fact]
    public void ARewrittenExpiry_DoesNotBuyTime()
    {
        using var key = Key();
        var code = SampleCode();
        var offer = Offer(key, code, TimeSpan.FromMinutes(1));

        BinaryPrimitives.WriteInt64LittleEndian(offer.AsSpan(66), Noon.AddMinutes(59).ToUnixTimeMilliseconds());

        RefusalOf(() => SyncPairingOffer.Open(offer, code, Accepting, Noon.AddMinutes(30)))
            .Should().Be(SyncPairingRefusal.NotAuthentic, "the expiry is inside the associated data");
    }

    [Fact]
    public void AForgedKeyId_IsCaughtByTheTag()
    {
        using var key = Key(1);
        using var other = Key(2);
        var code = SampleCode();
        var offer = Offer(key, code);

        other.KeyId.CopyTo(offer.AsSpan(42, 16));

        RefusalOf(() => SyncPairingOffer.Open(offer, code, Accepting, Noon)).Should().Be(SyncPairingRefusal.NotAuthentic);
    }

    [Fact]
    public void CreateCore_RefusesAHeaderNoOffererCouldMean()
    {
        using var key = Key();
        var code = SampleCode();
        var good = new SyncPairingOfferHeader(1, Offering, Guid.NewGuid(), key.KeyId, Noon, Noon.AddMinutes(5));

        FluentActions.Invoking(() => SyncPairingOffer.CreateCore(good with { OfferingVaultId = Guid.Empty }, new byte[32], code, Salt(), Nonce()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncPairingOffer.CreateCore(good with { OfferId = Guid.Empty }, new byte[32], code, Salt(), Nonce()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncPairingOffer.CreateCore(good with { ExpiresAtUtc = Noon }, new byte[32], code, Salt(), Nonce()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncPairingOffer.CreateCore(good with { ExpiresAtUtc = Noon.AddDays(1) }, new byte[32], code, Salt(), Nonce()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncPairingOffer.CreateCore(good, new byte[31], code, Salt(), Nonce()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncPairingOffer.CreateCore(good, new byte[32], code, new byte[31], Nonce()))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncPairingOffer.CreateCore(good, new byte[32], code, Salt(), new byte[11]))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncPairingOffer.CreateCore(good, new byte[32], "not a code", Salt(), Nonce()))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_RefusesALifetimeAnOfferMayNotHave()
    {
        using var key = Key();
        var code = SampleCode();

        FluentActions.Invoking(() => Offer(key, code, TimeSpan.Zero)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Offer(key, code, TimeSpan.FromMinutes(-1))).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Offer(key, code, SyncPairingLimits.MaximumLifetime + TimeSpan.FromSeconds(1)))
            .Should().Throw<ArgumentOutOfRangeException>();
        Offer(key, code, SyncPairingLimits.MaximumLifetime).Should().HaveCount(SyncPairingOffer.Length);
    }

    [Fact]
    public void TheConstruction_IsTheOneTheDocumentDescribes()
    {
        using var key = Key();
        var code = SampleCode();
        var salt = Salt();
        var nonce = Nonce();
        var offerId = Guid.NewGuid();
        var header = new SyncPairingOfferHeader(1, Offering, offerId, key.KeyId, Noon, Noon.AddMinutes(10));

        var offer = SyncPairingOffer.CreateCore(header, Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), code, salt, nonce);

        // Rebuilt from the remarks alone, with .NET's own HKDF and AES-GCM and none of the class under test.
        var wrapKey = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.ASCII.GetBytes(SyncPairingCode.Normalise(code)!),
            wrapKey,
            salt,
            Encoding.ASCII.GetBytes("pine:sync-pairing:v1"));

        var expected = new byte[166];
        "PINEPAIR"u8.ToArray().CopyTo(expected, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(8), 1);
        Offering.TryWriteBytes(expected.AsSpan(10, 16));
        offerId.TryWriteBytes(expected.AsSpan(26, 16));
        key.KeyId.CopyTo(expected.AsSpan(42, 16));
        BinaryPrimitives.WriteInt64LittleEndian(expected.AsSpan(58), Noon.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteInt64LittleEndian(expected.AsSpan(66), Noon.AddMinutes(10).ToUnixTimeMilliseconds());
        salt.CopyTo(expected.AsSpan(74, 32));
        nonce.CopyTo(expected.AsSpan(106, 12));
        using (var aes = new AesGcm(wrapKey, 16))
        {
            aes.Encrypt(nonce, Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), expected.AsSpan(134, 32), expected.AsSpan(118, 16), expected.AsSpan(0, 118));
        }

        offer.Should().Equal(expected);
    }

    [Fact]
    public void NoRefusalMessage_QuotesTheCode_TheKeyOrTheFingerprint()
    {
        using var key = Key();
        using var other = Key(90);
        var code = SampleCode();
        var canonical = SyncPairingCode.Normalise(code)!;
        var offer = Offer(key, code, TimeSpan.FromMinutes(1));

        var tampered = (byte[])offer.Clone();
        tampered[140] ^= 0x01;
        var wrongVersion = (byte[])offer.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(wrongVersion.AsSpan(8), 9);
        var payloadHeader = new SyncPairingOfferHeader(1, Offering, Guid.NewGuid(), key.KeyId, Noon, Noon.AddMinutes(5));
        var lying = SyncPairingOffer.CreateCore(payloadHeader, other.KeyId.Concat(other.KeyId).ToArray(), code, Salt(), Nonce());

        var messages = new List<string>();
        foreach (var act in new Action[]
        {
            () => SyncPairingOffer.Open(offer, SampleCode(), Accepting, Noon),
            () => SyncPairingOffer.Open(tampered, code, Accepting, Noon),
            () => SyncPairingOffer.Open(offer, code, Accepting, Noon.AddHours(2)),
            () => SyncPairingOffer.Open(offer, code, Offering, Noon),
            () => SyncPairingOffer.Open(lying, code, Accepting, Noon),
            () => SyncPairingOffer.Open(wrongVersion, code, Accepting, Noon),
            () => SyncPairingOffer.Open(offer.AsSpan(0, 40).ToArray(), code, Accepting, Noon),
            () => SyncPairingOffer.Open(offer, "nope", Accepting, Noon),
        })
        {
            var refusal = act.Should().Throw<SyncPairingRefusedException>().Which;
            refusal.InnerException.Should().BeNull("which of the failures it was is what an attacker wants to know");
            messages.Add(refusal.Message);
        }

        messages.Should().HaveCount(8);
        foreach (var message in messages)
        {
            message.Should().NotContain(canonical).And.NotContain(code);
            message.Should().NotContain(key.Fingerprint).And.NotContain(Convert.ToHexString(key.KeyId));
            message.Should().NotMatchRegex("[0-9A-Fa-f]{24}", "no message may carry a run of key material");

            foreach (var character in canonical.Chunk(5).Select(chunk => new string(chunk)))
            {
                message.Should().NotContain(character, "not even a fifth of the code");
            }
        }
    }

    [Fact]
    public void AKeysToString_NamesTheKeyId_AndNeverTheMaterialOrTheFingerprint()
    {
        using var key = Key();

        key.ToString().Should().Be("SyncKey " + Convert.ToHexString(key.KeyId));
        key.ToString().Should().NotContain(key.Fingerprint);
        key.ToString().Should().NotContain(Convert.ToHexString(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
    }

    [Fact]
    public void ARefusalSaysTheSameThingToTheUserAsToTheLog()
    {
        foreach (var refusal in Enum.GetValues<SyncPairingRefusal>())
        {
            var exception = new SyncPairingRefusedException(refusal);
            exception.Refusal.Should().Be(refusal);
            exception.Message.Should().NotBeNullOrWhiteSpace();
            exception.UserFacingMessages.Should().ContainSingle()
                .Which.Key.Should().Be(SyncPairingRefusedException.Describe(refusal).Key);
        }

        FluentActions.Invoking(() => SyncPairingRefusedException.Describe((SyncPairingRefusal)99))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    private static byte[] Salt() => RandomNumberGenerator.GetBytes(32);

    private static byte[] Nonce() => RandomNumberGenerator.GetBytes(12);

    /// <summary>Header fields <c>ReadHeader</c> judges before the tag is checked.</summary>
    private static bool IsHeaderShapeField(int index) =>
        index is >= 10 and < 42  // the two guids, which may become empty
        || index is >= 58 and < 74; // the two times, which may become a non-time or an impossible life

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}
