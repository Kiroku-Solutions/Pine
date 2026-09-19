using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Pine.Infrastructure.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>
/// §5's envelope (post-audit plan item 3.1d): the change set sealed under the user's sync key.
/// </summary>
/// <remarks>
/// THE REFUSALS ARE THE POINT, as they are for <c>VaultFileCipherTests</c>: a round trip proves only that the class
/// can talk to itself. The layout is written out here as literal offsets, not read from the production constants, so
/// a change to the format is a failing test rather than a silent re-agreement.
/// </remarks>
public class SyncEnvelopeTests
{
    private const int HeaderLength = 126;
    private const int KeyIdOffset = 66;
    private const int SaltOffset = 82;
    private const int NonceOffset = 114;
    private const int TagOffset = 126;
    private const int CiphertextOffset = 142;

    private static readonly Guid Source = Guid.Parse("5A5A5A5A-0000-4000-8000-000000000001");
    private static readonly Guid Target = Guid.Parse("7B7B7B7B-0000-4000-8000-000000000002");
    private static readonly Guid Other = Guid.Parse("9C9C9C9C-0000-4000-8000-000000000003");
    private static readonly Guid Device = Guid.Parse("D1D1D1D1-0000-4000-8000-000000000004");
    private static readonly DateTimeOffset Sealed = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static SyncKey KeyA() => SyncKey.FromBytes(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static SyncKey KeyB() => SyncKey.FromBytes(Enumerable.Range(101, 32).Select(i => (byte)i).ToArray());

    private static SyncChangeSet ChangeSet(Guid? target = null, long since = SyncChangeSet.Never, long sourceClock = 42, string payee = "Grocer Secretive")
    {
        var id = Guid.Parse("11111111-0000-4000-8000-000000000000");
        return new SyncChangeSet(
            SyncChangeSet.FormatName,
            "20260918145142_AddSyncMergeState",
            Source,
            target ?? Target,
            since,
            sourceClock,
            7,
            [
                new SyncChange("Transactions", id, 3, Device, IsTombstone: false, null,
                    new SyncRow(new Dictionary<string, object?> { ["Id"] = SyncRow.GuidText(id), ["Payee"] = payee, ["TotalAmount_Amount"] = -200_000L }),
                    new Dictionary<string, IReadOnlyList<SyncRow>>()),
            ]);
    }

    private static SyncEnvelopeRefusal RefusalOf(Action open)
    {
        var refusal = open.Should().Throw<SyncEnvelopeRefusedException>().Which;
        refusal.InnerException.Should().BeNull("a refusal carries nothing that could quote or characterise the payload");
        return refusal.Reason;
    }

    [Fact]
    public void AChangeSet_SurvivesSealAndOpen_AndTheEnvelopeHoldsNoPlaintext()
    {
        using var key = KeyA();
        var changeSet = ChangeSet();

        var envelope = SyncEnvelope.Seal(changeSet, key, Sealed);
        var opened = SyncEnvelope.Open(envelope, key, Target);

        opened.ToJson().Should().Be(changeSet.ToJson());
        envelope.Length.Should().Be(CiphertextOffset + Encoding.UTF8.GetByteCount(changeSet.ToJson()), "no compression, no padding");
        Encoding.UTF8.GetString(envelope).Should().NotContain("Secretive").And.NotContain("Transactions").And.NotContain("pine.sync.v1");
    }

    [Fact]
    public void TheHeader_SaysWhatAProviderMaySee_AndNothingMore()
    {
        using var key = KeyA();
        var envelope = SyncEnvelope.Seal(ChangeSet(since: 5, sourceClock: 42), key, Sealed.AddTicks(1234));

        var header = SyncEnvelope.ReadHeader(envelope);

        header.Version.Should().Be(1);
        header.SourceVaultId.Should().Be(Source);
        header.TargetVaultId.Should().Be(Target);
        header.SinceClock.Should().Be(5);
        header.SourceClock.Should().Be(42);
        header.CreatedAtUtc.Should().Be(Sealed, "milliseconds; the sub-millisecond ticks are not kept");
        header.KeyId.Should().Equal(key.KeyId);
        envelope.AsSpan(0, 8).ToArray().Should().Equal("PINESYNC"u8.ToArray());
        BinaryPrimitives.ReadUInt16LittleEndian(envelope.AsSpan(8)).Should().Be(1);
    }

    [Fact]
    public void EverySeal_DrawsAFreshSaltAndNonce()
    {
        using var key = KeyA();
        var changeSet = ChangeSet();
        var salts = new HashSet<string>(StringComparer.Ordinal);
        var nonces = new HashSet<string>(StringComparer.Ordinal);
        var ciphertexts = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 500; i++)
        {
            var envelope = SyncEnvelope.Seal(changeSet, key, Sealed);
            salts.Add(Convert.ToHexString(envelope, SaltOffset, 32));
            nonces.Add(Convert.ToHexString(envelope, NonceOffset, 12));
            ciphertexts.Add(Convert.ToHexString(envelope, CiphertextOffset, envelope.Length - CiphertextOffset));
        }

        salts.Should().HaveCount(500);
        nonces.Should().HaveCount(500);
        ciphertexts.Should().HaveCount(500, "one plaintext sealed twice is two unrelated ciphertexts");
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x80)]
    public void AFlippedBit_AnywhereInTheEnvelope_IsRefused_ForTheReasonItsRegionGives(int mask)
    {
        using var key = KeyA();
        var envelope = SyncEnvelope.Seal(ChangeSet(since: 3), key, Sealed);

        for (var index = 0; index < envelope.Length; index++)
        {
            var tampered = (byte[])envelope.Clone();
            tampered[index] ^= (byte)mask;

            var reason = RefusalOf(() => SyncEnvelope.Open(tampered, key, Target));

            var expected = index switch
            {
                < 8 => new[] { SyncEnvelopeRefusal.Malformed },
                < 10 => [SyncEnvelopeRefusal.UnsupportedVersion],
                < 42 => [SyncEnvelopeRefusal.NotAuthentic],
                < KeyIdOffset => [SyncEnvelopeRefusal.Malformed, SyncEnvelopeRefusal.NotAuthentic], // a cursor or time made impossible is refused on its shape
                < SaltOffset => [SyncEnvelopeRefusal.WrongKey],
                _ => [SyncEnvelopeRefusal.NotAuthentic],
            };
            expected.Should().Contain(reason, $"byte {index} flipped with 0x{mask:X2}");
        }
    }

    [Fact]
    public void ATruncatedEnvelope_IsRefused_AtEveryLength()
    {
        using var key = KeyA();
        var envelope = SyncEnvelope.Seal(ChangeSet(), key, Sealed);

        for (var length = 0; length < envelope.Length; length++)
        {
            var truncated = envelope.AsSpan(0, length).ToArray();
            RefusalOf(() => SyncEnvelope.Open(truncated, key, Target))
                .Should().Be(length < CiphertextOffset ? SyncEnvelopeRefusal.Malformed : SyncEnvelopeRefusal.NotAuthentic, $"length {length}");
        }
    }

    [Fact]
    public void AnEnvelopeWithAByteAppended_IsRefused()
    {
        using var key = KeyA();
        var envelope = SyncEnvelope.Seal(ChangeSet(), key, Sealed);

        RefusalOf(() => SyncEnvelope.Open([.. envelope, 0], key, Target)).Should().Be(SyncEnvelopeRefusal.NotAuthentic);
    }

    [Fact]
    public void TheWrongKey_IsRefusedAsTheWrongKey_AndAForgedKeyIdDoesNotGetPastTheTag()
    {
        using var key = KeyA();
        using var other = KeyB();
        var envelope = SyncEnvelope.Seal(ChangeSet(), key, Sealed);

        RefusalOf(() => SyncEnvelope.Open(envelope, other, Target)).Should().Be(SyncEnvelopeRefusal.WrongKey);

        var forged = (byte[])envelope.Clone();
        other.KeyId.CopyTo(forged, KeyIdOffset);
        RefusalOf(() => SyncEnvelope.Open(forged, other, Target)).Should().Be(SyncEnvelopeRefusal.NotAuthentic);
    }

    [Fact]
    public void AHeaderMovedOntoAnotherEnvelopesBody_IsRefused()
    {
        using var key = KeyA();
        var first = SyncEnvelope.Seal(ChangeSet(sourceClock: 42), key, Sealed);
        var second = SyncEnvelope.Seal(ChangeSet(sourceClock: 43), key, Sealed);
        first.Length.Should().Be(second.Length);

        var spliced = first.AsSpan(0, HeaderLength).ToArray().Concat(second.AsSpan(HeaderLength).ToArray()).ToArray();
        RefusalOf(() => SyncEnvelope.Open(spliced, key, Target)).Should().Be(SyncEnvelopeRefusal.NotAuthentic);

        // Only the cursor rewritten, nonce and salt kept: still the tag.
        var rewritten = (byte[])first.Clone();
        BinaryPrimitives.WriteInt64LittleEndian(rewritten.AsSpan(50), 43);
        RefusalOf(() => SyncEnvelope.Open(rewritten, key, Target)).Should().Be(SyncEnvelopeRefusal.NotAuthentic);
    }

    [Fact]
    public void AnEnvelopeForAnotherVault_IsRefused_AfterItIsAuthenticated()
    {
        using var key = KeyA();
        var envelope = SyncEnvelope.Seal(ChangeSet(target: Other), key, Sealed);

        RefusalOf(() => SyncEnvelope.Open(envelope, key, Target)).Should().Be(SyncEnvelopeRefusal.WrongTarget);
        RefusalOf(() => SyncEnvelope.Open(envelope, key, Guid.Empty)).Should().Be(SyncEnvelopeRefusal.WrongTarget, "a vault with no id yet is no envelope's target");
        SyncEnvelope.Open(envelope, key, Other).TargetVaultId.Should().Be(Other);
    }

    [Fact]
    public void APayloadThatDisagreesWithItsHeader_IsRefused_EvenThoughItIsAuthentic()
    {
        using var key = KeyA();
        var header = new SyncEnvelopeHeader(1, Source, Target, SyncChangeSet.Never, 42, Sealed, key.KeyId);
        var salt = new byte[32];
        var nonce = new byte[12];

        foreach (var inner in new[] { ChangeSet(target: Other), ChangeSet(since: 1), ChangeSet(sourceClock: 41), ChangeSet() with { SourceVaultId = Other } })
        {
            var envelope = SyncEnvelope.SealCore(header, SyncChangeSet.FormatName, inner.ToUtf8Json(), key, salt, nonce);
            RefusalOf(() => SyncEnvelope.Open(envelope, key, Target)).Should().Be(SyncEnvelopeRefusal.PayloadMismatch);
        }

        var notJson = SyncEnvelope.SealCore(header, SyncChangeSet.FormatName, "not a change set"u8, key, salt, nonce);
        RefusalOf(() => SyncEnvelope.Open(notJson, key, Target)).Should().Be(SyncEnvelopeRefusal.PayloadMismatch);

        var agreeing = SyncEnvelope.SealCore(header, SyncChangeSet.FormatName, ChangeSet().ToUtf8Json(), key, salt, nonce);
        SyncEnvelope.Open(agreeing, key, Target).SourceClock.Should().Be(42);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(0)]
    public void AnotherVersion_IsRefused(int version)
    {
        using var key = KeyA();
        var envelope = SyncEnvelope.Seal(ChangeSet(), key, Sealed);
        BinaryPrimitives.WriteUInt16LittleEndian(envelope.AsSpan(8), (ushort)version);

        RefusalOf(() => SyncEnvelope.Open(envelope, key, Target)).Should().Be(SyncEnvelopeRefusal.UnsupportedVersion);
    }

    [Fact]
    public void AHeaderNoSealerWrites_IsRefusedOnItsShape()
    {
        using var key = KeyA();
        var envelope = SyncEnvelope.Seal(ChangeSet(since: 3, sourceClock: 42), key, Sealed);

        void Refused(Action<byte[]> edit)
        {
            var copy = (byte[])envelope.Clone();
            edit(copy);
            RefusalOf(() => SyncEnvelope.ReadHeader(copy)).Should().Be(SyncEnvelopeRefusal.Malformed);
        }

        Refused(e => Guid.Empty.TryWriteBytes(e.AsSpan(10, 16)));
        Refused(e => Guid.Empty.TryWriteBytes(e.AsSpan(26, 16)));
        Refused(e => e.AsSpan(10, 16).CopyTo(e.AsSpan(26, 16)));
        Refused(e => BinaryPrimitives.WriteInt64LittleEndian(e.AsSpan(42), -2));
        Refused(e => BinaryPrimitives.WriteInt64LittleEndian(e.AsSpan(42), 43));
        Refused(e => BinaryPrimitives.WriteInt64LittleEndian(e.AsSpan(50), -1));
        Refused(e => BinaryPrimitives.WriteInt64LittleEndian(e.AsSpan(58), long.MaxValue));
        Refused(e => e[0] = (byte)'X');

        // The boundaries that ARE written: "everything" (-1) up to a fresh vault's 0, and since == source clock.
        var fromNothing = SyncEnvelope.Seal(ChangeSet(since: SyncChangeSet.Never, sourceClock: 0), key, Sealed);
        SyncEnvelope.ReadHeader(fromNothing).SourceClock.Should().Be(0);
        var nothingNew = SyncEnvelope.Seal(ChangeSet(since: 42, sourceClock: 42), key, Sealed);
        SyncEnvelope.ReadHeader(nothingNew).SinceClock.Should().Be(42);
    }

    [Fact]
    public void Seal_RefusesAChangeSetNoEnvelopeCarries()
    {
        using var key = KeyA();

        FluentActions.Invoking(() => SyncEnvelope.Seal(ChangeSet() with { Format = "pine.sync.v2" }, key, Sealed)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncEnvelope.Seal(ChangeSet(target: Guid.Empty), key, Sealed)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncEnvelope.Seal(ChangeSet() with { SourceVaultId = Guid.Empty }, key, Sealed)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncEnvelope.Seal(ChangeSet(target: Source), key, Sealed)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncEnvelope.Seal(ChangeSet(since: 43, sourceClock: 42), key, Sealed)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncEnvelope.Seal(ChangeSet(since: -2), key, Sealed)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SyncEnvelope.Seal(ChangeSet(since: -1, sourceClock: -1), key, Sealed)).Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The envelope, opened by hand from the layout the documentation gives — HKDF-SHA256 with the envelope's salt and
    /// <c>pine:sync-envelope:v1</c>, then AES-256-GCM with the whole header as associated data — without the class.
    /// </summary>
    [Fact]
    public void TheDocumentedConstruction_OpensTheEnvelope_WithoutTheClass()
    {
        var material = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        using var key = SyncKey.FromBytes(material);
        var changeSet = ChangeSet();
        var envelope = SyncEnvelope.Seal(changeSet, key, Sealed);

        var keyId = HKDF.DeriveKey(HashAlgorithmName.SHA256, material, 16, salt: [], info: Encoding.ASCII.GetBytes("pine:sync-key-id:v1"));
        envelope.AsSpan(KeyIdOffset, 16).ToArray().Should().Equal(keyId);

        var envelopeKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, material, 32, envelope.AsSpan(SaltOffset, 32).ToArray(), Encoding.ASCII.GetBytes("pine:sync-envelope:v1"));
        var plaintext = new byte[envelope.Length - CiphertextOffset];
        using (var aes = new AesGcm(envelopeKey, 16))
        {
            aes.Decrypt(envelope.AsSpan(NonceOffset, 12), envelope.AsSpan(CiphertextOffset), envelope.AsSpan(TagOffset, 16), plaintext, envelope.AsSpan(0, HeaderLength));
        }

        Encoding.UTF8.GetString(plaintext).Should().Be(changeSet.ToJson());
    }

    /// <summary>
    /// A known-answer vector. `14-Synchronization` defines none, so this one is Pine's own — and it was not taken from
    /// this implementation's output: on 2026-09-19 it was computed separately in Python (HKDF-SHA256 from the standard
    /// library's HMAC; AES-256 block encryptions from OpenSSL 1.1.1's ECB mode; GCM's counter mode and GHASH written
    /// out by hand) from the layout in <see cref="SyncEnvelope"/>'s remarks, and the two agreed byte for byte. Any change
    /// to the format, the key derivation or the binding breaks it.
    /// </summary>
    [Fact]
    public void AKnownAnswer_IsReproducedExactly()
    {
        using var key = KeyA();
        var header = new SyncEnvelopeHeader(1, Source, Target, SyncChangeSet.Never, 42, Sealed, key.KeyId);
        var salt = Enumerable.Range(0x40, 32).Select(i => (byte)i).ToArray();
        var nonce = Enumerable.Range(0x80, 12).Select(i => (byte)i).ToArray();

        var envelope = SyncEnvelope.SealCore(header, SyncChangeSet.FormatName, "{}"u8, key, salt, nonce);

        Convert.ToHexString(key.KeyId).Should().Be(KnownKeyId);
        Convert.ToHexString(envelope).Should().Be(KnownEnvelope);
    }

    private const string KnownKeyId = "EACE54060E0974B4D28763C22EA80D05";
    private const string KnownEnvelope =
        "50494E4553594E4301005A5A5A5A0000004080000000000000017B7B7B7B000000408000000000000002FFFFFFFFFFFFFFFF2A00000000000000" +
        "00A289B9A0010000EACE54060E0974B4D28763C22EA80D05404142434445464748494A4B4C4D4E4F505152535455565758595A5B5C5D5E5F8081" +
        "82838485868788898A8B15697E54999E430B710C5DA8A9B82B02AFCA";
}
