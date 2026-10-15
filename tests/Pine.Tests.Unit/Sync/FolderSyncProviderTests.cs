using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Pine.Core.Sync;
using Pine.Infrastructure.Sync;
using Xunit;

namespace Pine.Tests.Unit.Sync;

/// <summary>The folder a sync run leaves envelopes in and takes them from (post-audit plan item 3.1f).</summary>
public sealed class FolderSyncProviderTests : IDisposable
{
    private static readonly Guid Left = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
    private static readonly Guid Right = Guid.Parse("bbbbbbbb-2222-4222-8222-222222222222");
    private static readonly string KeyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "FolderSyncProviderTests", Guid.NewGuid().ToString("N"));
    private readonly CancellationToken _ct = CancellationToken.None;

    public FolderSyncProviderTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory is not worth failing a green test over.
        }
    }

    private FolderSyncProvider Provider() => new(_folder);

    private static SyncEnvelopeAddress LeftToRight => new(Left, Right);

    [Fact]
    public async Task AFolderThatIsThere_IsReachable_AndOneThatIsNot_IsNot()
    {
        (await Provider().HealthAsync(_ct)).Should().Be(SyncProviderHealth.Reachable);
        (await new FolderSyncProvider(Path.Combine(_folder, "unplugged")).HealthAsync(_ct)).Should().Be(SyncProviderHealth.Unreachable);
        Directory.Exists(Path.Combine(_folder, "unplugged")).Should().BeFalse("asking whether a folder is there never creates it");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative\\folder")]
    [InlineData("relative/folder")]
    public void ARelativeOrEmptyPath_IsNotAFolder(string path)
    {
        FluentActions.Invoking(() => new FolderSyncProvider(path)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AnEnvelope_ComesBackExactlyAsItWasLeft_UnderANameOfIdsOnly()
    {
        var envelope = RandomNumberGenerator.GetBytes(500);
        var provider = Provider();

        await provider.UploadAsync(LeftToRight, envelope, _ct);

        (await provider.DownloadAsync(LeftToRight, _ct)).Should().Equal(envelope);
        (await provider.DownloadAsync(new SyncEnvelopeAddress(Right, Left), _ct)).Should().BeNull("an address is one direction");

        Directory.GetFiles(_folder).Select(Path.GetFileName).Should().Equal(
            ["aaaaaaaa111141118111111111111111.bbbbbbbb222242228222222222222222.pine-envelope"]);
    }

    [Fact]
    public async Task ANewerEnvelope_ReplacesTheOlder_AndLeavesNoTemporaryFile()
    {
        var provider = Provider();
        await provider.UploadAsync(LeftToRight, new byte[] { 1, 2, 3 }, _ct);
        await provider.UploadAsync(LeftToRight, new byte[] { 4, 5 }, _ct);

        (await provider.DownloadAsync(LeftToRight, _ct)).Should().Equal(4, 5);
        Directory.GetFiles(_folder).Should().ContainSingle();
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatAddress_AndSaysWhetherThereWasOne()
    {
        var provider = Provider();
        await provider.UploadAsync(LeftToRight, new byte[] { 1 }, _ct);
        await provider.UploadAsync(new SyncEnvelopeAddress(Right, Left), new byte[] { 2 }, _ct);

        (await provider.DeleteAsync(LeftToRight, _ct)).Should().BeTrue();
        (await provider.DeleteAsync(LeftToRight, _ct)).Should().BeFalse();

        (await provider.DownloadAsync(LeftToRight, _ct)).Should().BeNull();
        (await provider.DownloadAsync(new SyncEnvelopeAddress(Right, Left), _ct)).Should().Equal(2);
    }

    [Fact]
    public async Task AFileLargerThanAnyEnvelope_IsRefused_WithoutBeingRead()
    {
        var path = Path.Combine(_folder, FolderSyncProvider.EnvelopeFileName(LeftToRight));
        using (var stream = File.Create(path))
        {
            // Sparse where the file system allows: the length is what is measured.
            stream.SetLength(SyncProviderLimits.MaximumEnvelopeBytes + 1);
        }

        await FluentActions.Awaiting(() => Provider().DownloadAsync(LeftToRight, _ct))
            .Should().ThrowAsync<SyncEnvelopeTooLargeException>();
    }

    [Fact]
    public async Task AnEnvelopeExactlyAtTheLimit_IsRead()
    {
        var path = Path.Combine(_folder, FolderSyncProvider.EnvelopeFileName(LeftToRight));
        using (var stream = File.Create(path))
        {
            stream.SetLength(SyncProviderLimits.MaximumEnvelopeBytes);
        }

        (await Provider().DownloadAsync(LeftToRight, _ct))!.Length.Should().Be((int)SyncProviderLimits.MaximumEnvelopeBytes);
    }

    [Fact]
    public async Task AnEnvelopeLargerThanTheLimit_IsNotWritten()
    {
        var tooLarge = new byte[SyncProviderLimits.MaximumEnvelopeBytes + 1];

        await FluentActions.Awaiting(() => Provider().UploadAsync(LeftToRight, tooLarge, _ct))
            .Should().ThrowAsync<SyncEnvelopeTooLargeException>();
        Directory.GetFiles(_folder).Should().BeEmpty();
    }

    [Fact]
    public async Task AnAddressThatIsNotFromOneVaultToAnother_IsRefused()
    {
        var provider = Provider();
        foreach (var address in new[] { new SyncEnvelopeAddress(Left, Left), new SyncEnvelopeAddress(Guid.Empty, Right), new SyncEnvelopeAddress(Left, Guid.Empty) })
        {
            await FluentActions.Awaiting(() => provider.UploadAsync(address, new byte[] { 1 }, _ct)).Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => provider.DownloadAsync(address, _ct)).Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => provider.DeleteAsync(address, _ct)).Should().ThrowAsync<ArgumentException>();
        }

        Directory.GetFiles(_folder).Should().BeEmpty();
    }

    [Fact]
    public async Task AVaultThatAnnouncesItself_IsListed_WithItsKeyId()
    {
        var provider = Provider();
        await provider.AnnounceAsync(new SyncPeerAnnouncement(Left, KeyId), _ct);
        await provider.AnnounceAsync(new SyncPeerAnnouncement(Right, KeyId), _ct);

        (await provider.ListPeersAsync(_ct)).Should().Equal(
            new SyncPeerAnnouncement(Left, KeyId), new SyncPeerAnnouncement(Right, KeyId));
        File.ReadAllBytes(Path.Combine(_folder, FolderSyncProvider.AnnouncementFileName(Left)))
            .Should().HaveCount(FolderSyncProvider.AnnouncementLength);
    }

    [Fact]
    public async Task AnnouncingAgain_DoesNotRewriteTheFile()
    {
        var provider = Provider();
        await provider.AnnounceAsync(new SyncPeerAnnouncement(Left, KeyId), _ct);
        var path = Path.Combine(_folder, FolderSyncProvider.AnnouncementFileName(Left));
        var written = DateTime.UtcNow.AddDays(-3);
        File.SetLastWriteTimeUtc(path, written);

        await provider.AnnounceAsync(new SyncPeerAnnouncement(Left, KeyId), _ct);

        File.GetLastWriteTimeUtc(path).Should().Be(written, "a folder another program mirrors is not touched on every run");
    }

    [Fact]
    public async Task AnAnnouncementUnderAnotherKey_ReplacesTheOld()
    {
        var provider = Provider();
        var other = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await provider.AnnounceAsync(new SyncPeerAnnouncement(Left, KeyId), _ct);
        await provider.AnnounceAsync(new SyncPeerAnnouncement(Left, other), _ct);

        (await provider.ListPeersAsync(_ct)).Should().Equal(new SyncPeerAnnouncement(Left, other));
    }

    [Fact]
    public async Task WhatIsNotAnAnnouncement_IsNotAPeer()
    {
        var provider = Provider();
        await provider.AnnounceAsync(new SyncPeerAnnouncement(Left, KeyId), _ct);
        var good = File.ReadAllBytes(Path.Combine(_folder, FolderSyncProvider.AnnouncementFileName(Left)));

        // A name that is not an id, a copy a sync tool renamed, a file under one vault's name naming another,
        // a short file, the wrong magic, another version, and an empty vault id.
        File.WriteAllBytes(Path.Combine(_folder, "not-an-id.pine-vault"), good);
        File.WriteAllBytes(Path.Combine(_folder, FolderSyncProvider.AnnouncementFileName(Left)[..32] + " (conflicted copy).pine-vault"), good);
        File.WriteAllBytes(Path.Combine(_folder, FolderSyncProvider.AnnouncementFileName(Right)), good);

        var shortFile = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(_folder, FolderSyncProvider.AnnouncementFileName(shortFile)), good[..^1]);

        var badMagic = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(_folder, FolderSyncProvider.AnnouncementFileName(badMagic)), Mutated(FolderSyncProvider.Encode(new SyncPeerAnnouncement(badMagic, KeyId)), 0));

        var badVersion = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(_folder, FolderSyncProvider.AnnouncementFileName(badVersion)), Mutated(FolderSyncProvider.Encode(new SyncPeerAnnouncement(badVersion, KeyId)), 8));

        var emptyId = FolderSyncProvider.Encode(new SyncPeerAnnouncement(Left, KeyId));
        Array.Clear(emptyId, 10, 16);
        File.WriteAllBytes(Path.Combine(_folder, FolderSyncProvider.AnnouncementFileName(Guid.Empty)), emptyId);

        (await provider.ListPeersAsync(_ct)).Should().Equal(new SyncPeerAnnouncement(Left, KeyId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCD")]
    [InlineData("not hexadecimal at all, not at all")]
    public void AnAnnouncementWithAKeyIdThatIsNotOne_IsNotWritten(string keyId)
    {
        FluentActions.Invoking(() => FolderSyncProvider.Encode(new SyncPeerAnnouncement(Left, keyId))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AnAnnouncementForNoVault_IsNotWritten()
    {
        FluentActions.Invoking(() => FolderSyncProvider.Encode(new SyncPeerAnnouncement(Guid.Empty, KeyId))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AnAnnouncement_RoundTrips()
    {
        FolderSyncProvider.Decode(FolderSyncProvider.Encode(new SyncPeerAnnouncement(Right, KeyId)))
            .Should().Be(new SyncPeerAnnouncement(Right, KeyId));
    }

    [Fact]
    public async Task AFolderThatGoesAway_FailsAsAFolderThatGoesAway()
    {
        var gone = Path.Combine(_folder, "gone");
        var provider = new FolderSyncProvider(gone);

        await FluentActions.Awaiting(() => provider.UploadAsync(LeftToRight, new byte[] { 1 }, _ct)).Should().ThrowAsync<IOException>();
        await FluentActions.Awaiting(() => provider.DownloadAsync(LeftToRight, _ct)).Should().ThrowAsync<IOException>();
        await FluentActions.Awaiting(() => provider.AnnounceAsync(new SyncPeerAnnouncement(Left, KeyId), _ct)).Should().ThrowAsync<IOException>();
        Directory.Exists(gone).Should().BeFalse();
    }

    private static byte[] Mutated(byte[] bytes, int index)
    {
        bytes[index] ^= 0x01;
        return bytes;
    }

    // ------------------------------------------------------------------------------------------
    // §11, §13's right to erasure (post-audit plan item 3.1h).
    // ------------------------------------------------------------------------------------------

    [Fact]
    public async Task ListEnvelopesFrom_FindsOnlyWhatThatVaultLeft_NeverWhatWasLeftForIt()
    {
        var provider = Provider();
        var third = Guid.Parse("cccccccc-3333-4333-8333-333333333333");
        await provider.UploadAsync(new SyncEnvelopeAddress(Left, Right), new byte[] { 1 }, _ct);
        await provider.UploadAsync(new SyncEnvelopeAddress(Left, third), new byte[] { 2 }, _ct);
        await provider.UploadAsync(new SyncEnvelopeAddress(Right, Left), new byte[] { 3 }, _ct); // left FOR Left, not BY it

        var found = await provider.ListEnvelopesFromAsync(Left, _ct);

        found.Should().Equal(new SyncEnvelopeAddress(Left, Right), new SyncEnvelopeAddress(Left, third));
    }

    [Fact]
    public async Task ListEnvelopesFrom_IgnoresAFileThatDoesNotParseAsOneOfItsOwn()
    {
        var provider = Provider();
        await provider.UploadAsync(LeftToRight, new byte[] { 1 }, _ct);
        File.WriteAllBytes(Path.Combine(_folder, Guid.NewGuid().ToString("N") + " (conflicted copy).pine-envelope"), new byte[] { 9 });

        var found = await provider.ListEnvelopesFromAsync(Left, _ct);

        found.Should().Equal(LeftToRight);
    }

    [Fact]
    public async Task ListEnvelopesFrom_WithNoVaultId_IsRefused()
    {
        await FluentActions.Awaiting(() => Provider().ListEnvelopesFromAsync(Guid.Empty, _ct)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RetractAnnouncement_RemovesOnlyThatVaultsAnnouncement_AndSaysWhetherTherWasOne()
    {
        var provider = Provider();
        await provider.AnnounceAsync(new SyncPeerAnnouncement(Left, KeyId), _ct);
        await provider.AnnounceAsync(new SyncPeerAnnouncement(Right, KeyId), _ct);

        (await provider.RetractAnnouncementAsync(Left, _ct)).Should().BeTrue();
        (await provider.RetractAnnouncementAsync(Left, _ct)).Should().BeFalse("it was already gone");

        (await provider.ListPeersAsync(_ct)).Should().Equal(new SyncPeerAnnouncement(Right, KeyId));
    }

    [Fact]
    public async Task RetractAnnouncement_WithNoVaultId_IsRefused()
    {
        await FluentActions.Awaiting(() => Provider().RetractAnnouncementAsync(Guid.Empty, _ct)).Should().ThrowAsync<ArgumentException>();
    }
}
