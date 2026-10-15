using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Sync;

namespace Pine.Infrastructure.Sync;

/// <summary>
/// <see cref="ISyncProvider"/> over a folder: a USB drive carried between machines, a network share, or a folder some
/// other program keeps in step (post-audit plan item 3.1f; `14-Synchronization` §8).
/// </summary>
/// <remarks>
/// <para>
/// PINE OPENS NO CONNECTION FOR THIS. It reads and writes files under one directory the user chose; how those files
/// reach another machine is the user's channel, exactly as it is for a pairing offer. ADR-0012 stands.
/// </para>
/// <para>
/// THE LAYOUT, ALL OF IT IN ONE DIRECTORY, NOTHING ELSE TOUCHED.
/// </para>
/// <list type="bullet">
/// <item><description><c>{vault}.pine-vault</c> — an announcement, 42 bytes: <c>PINEPEER</c>, a format version, the
/// vault id, and its sync key's public id. Not secret: the same two values are in every envelope header.</description></item>
/// <item><description><c>{source}.{target}.pine-envelope</c> — the newest envelope from one vault to another, exactly
/// as <c>SyncEnvelope</c> sealed it. One per address; a newer one replaces it.</description></item>
/// </list>
/// <para>
/// Every id is 32 lower-case hexadecimal digits (<c>Guid</c> format "N"), so no name a peer could choose escapes the
/// directory, and a file whose name does not parse — a copy a sync tool renamed "(conflicted copy)", a temporary file —
/// is not anybody's envelope.
/// </para>
/// <para>
/// ERASURE (post-audit plan item 3.1h; §11, §13) NEEDS NO THIRD FILE KIND. <see cref="ListEnvelopesFromAsync"/>
/// finds every <c>{source}.*.pine-envelope</c> this vault left by parsing the same file names
/// <see cref="EnvelopeFileName"/> writes, and <see cref="RetractAnnouncementAsync"/> deletes the one announcement
/// a vault id ever has — the layout above is already everything an erasure needs to find.
/// </para>
/// <para>
/// WRITES ARE WHOLE OR ABSENT. Each file is written beside its final name, flushed, and moved over it, so a reader
/// on another machine sees the old envelope or the new one, never half of one. A write interrupted before the move
/// leaves a <c>.pine-tmp</c> file that nothing reads.
/// </para>
/// </remarks>
public sealed class FolderSyncProvider : ISyncProvider
{
    /// <summary>The extension of an announcement.</summary>
    public const string AnnouncementExtension = ".pine-vault";

    /// <summary>The extension of an envelope.</summary>
    public const string EnvelopeExtension = ".pine-envelope";

    /// <summary>An announcement's length: magic, version, vault id, key id.</summary>
    public const int AnnouncementLength = 8 + 2 + 16 + KeyIdLength;

    private const ushort AnnouncementVersion = 1;
    private const int KeyIdLength = 16;
    private const string TemporaryExtension = ".pine-tmp";
    private static readonly byte[] Magic = "PINEPEER"u8.ToArray();

    /// <summary>Creates the provider over one folder. Nothing is read or created until it is used.</summary>
    /// <param name="folderPath">The folder; a fully qualified path.</param>
    /// <exception cref="ArgumentException">The path is empty or not fully qualified.</exception>
    public FolderSyncProvider(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        if (!Path.IsPathFullyQualified(folderPath))
        {
            throw new ArgumentException("A sync folder is a fully qualified path.", nameof(folderPath));
        }

        FolderPath = Path.GetFullPath(folderPath);
    }

    /// <summary>The folder.</summary>
    public string FolderPath { get; }

    /// <summary>The file name an envelope for <paramref name="address"/> has.</summary>
    public static string EnvelopeFileName(SyncEnvelopeAddress address) =>
        Id(address.SourceVaultId) + "." + Id(address.TargetVaultId) + EnvelopeExtension;

    /// <summary>The file name a vault's announcement has.</summary>
    public static string AnnouncementFileName(Guid vaultId) => Id(vaultId) + AnnouncementExtension;

    /// <inheritdoc />
    public Task<SyncProviderHealth> HealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Directory.Exists(FolderPath) ? SyncProviderHealth.Reachable : SyncProviderHealth.Unreachable);
    }

    /// <inheritdoc />
    public Task AnnounceAsync(SyncPeerAnnouncement self, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(self);
        cancellationToken.ThrowIfCancellationRequested();

        var bytes = Encode(self);
        var path = Path.Combine(FolderPath, AnnouncementFileName(self.VaultId));

        // Written only when it would change, so a folder another program mirrors is not touched on every run.
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
        {
            WriteReplacing(path, bytes);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SyncPeerAnnouncement>> ListPeersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var peers = new List<SyncPeerAnnouncement>();
        foreach (var path in Directory.EnumerateFiles(FolderPath, "*" + AnnouncementExtension, SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (!name.EndsWith(AnnouncementExtension, StringComparison.Ordinal)
                || !Guid.TryParseExact(name[..^AnnouncementExtension.Length], "N", out var named))
            {
                continue;
            }

            byte[] bytes;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != AnnouncementLength)
                {
                    continue;
                }

                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // One unreadable file is not a reason to find nobody.
                continue;
            }

            if (Decode(bytes) is { } announcement && announcement.VaultId == named)
            {
                peers.Add(announcement);
            }
        }

        return Task.FromResult<IReadOnlyList<SyncPeerAnnouncement>>(peers.OrderBy(p => p.VaultId).ToList());
    }

    /// <inheritdoc />
    public Task UploadAsync(SyncEnvelopeAddress address, ReadOnlyMemory<byte> envelope, CancellationToken cancellationToken)
    {
        RefuseUnaddressed(address);
        cancellationToken.ThrowIfCancellationRequested();

        if (envelope.Length > SyncProviderLimits.MaximumEnvelopeBytes)
        {
            throw new SyncEnvelopeTooLargeException();
        }

        WriteReplacing(Path.Combine(FolderPath, EnvelopeFileName(address)), envelope.Span);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<byte[]?> DownloadAsync(SyncEnvelopeAddress address, CancellationToken cancellationToken)
    {
        RefuseUnaddressed(address);

        var path = Path.Combine(FolderPath, EnvelopeFileName(address));
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

            // Measured before anything is read, and read no further than the limit allows, so a file that grows
            // while it is read is refused rather than followed.
            if (stream.Length > SyncProviderLimits.MaximumEnvelopeBytes)
            {
                throw new SyncEnvelopeTooLargeException();
            }

            var bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (stream.ReadByte() != -1)
            {
                throw new SyncEnvelopeTooLargeException();
            }

            return bytes;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(SyncEnvelopeAddress address, CancellationToken cancellationToken)
    {
        RefuseUnaddressed(address);
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(FolderPath, EnvelopeFileName(address));
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SyncEnvelopeAddress>> ListEnvelopesFromAsync(Guid sourceVaultId, CancellationToken cancellationToken)
    {
        if (sourceVaultId == Guid.Empty)
        {
            throw new ArgumentException("A source vault id is not empty.", nameof(sourceVaultId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var prefix = Id(sourceVaultId) + ".";
        var addresses = new List<SyncEnvelopeAddress>();

        foreach (var path in Directory.EnumerateFiles(FolderPath, "*" + EnvelopeExtension, SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(EnvelopeExtension, StringComparison.Ordinal))
            {
                continue;
            }

            var middle = name[prefix.Length..^EnvelopeExtension.Length];
            if (Guid.TryParseExact(middle, "N", out var target) && target != Guid.Empty)
            {
                addresses.Add(new SyncEnvelopeAddress(sourceVaultId, target));
            }
        }

        return Task.FromResult<IReadOnlyList<SyncEnvelopeAddress>>(addresses.OrderBy(a => a.TargetVaultId).ToList());
    }

    /// <inheritdoc />
    public Task<bool> RetractAnnouncementAsync(Guid vaultId, CancellationToken cancellationToken)
    {
        if (vaultId == Guid.Empty)
        {
            throw new ArgumentException("A vault id is not empty.", nameof(vaultId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(FolderPath, AnnouncementFileName(vaultId));
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    /// <summary>An announcement's bytes.</summary>
    internal static byte[] Encode(SyncPeerAnnouncement announcement)
    {
        if (announcement.VaultId == Guid.Empty)
        {
            throw new ArgumentException("An announcement names a vault.", nameof(announcement));
        }

        byte[] keyId;
        try
        {
            keyId = Convert.FromHexString(announcement.KeyId ?? string.Empty);
        }
        catch (FormatException)
        {
            throw new ArgumentException("A key id is hexadecimal.", nameof(announcement));
        }

        if (keyId.Length != KeyIdLength)
        {
            throw new ArgumentException($"A key id is {KeyIdLength} bytes.", nameof(announcement));
        }

        var bytes = new byte[AnnouncementLength];
        Magic.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), AnnouncementVersion);
        announcement.VaultId.TryWriteBytes(bytes.AsSpan(10, 16));
        keyId.CopyTo(bytes, 26);
        return bytes;
    }

    /// <summary>An announcement read back, or null for anything this build did not write.</summary>
    internal static SyncPeerAnnouncement? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != AnnouncementLength
            || !bytes[..Magic.Length].SequenceEqual(Magic)
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]) != AnnouncementVersion)
        {
            return null;
        }

        var vaultId = new Guid(bytes.Slice(10, 16));
        return vaultId == Guid.Empty ? null : new SyncPeerAnnouncement(vaultId, Convert.ToHexString(bytes.Slice(26, KeyIdLength)));
    }

    private static string Id(Guid id) => id.ToString("N", CultureInfo.InvariantCulture);

    private static void RefuseUnaddressed(SyncEnvelopeAddress address)
    {
        if (address.SourceVaultId == Guid.Empty || address.TargetVaultId == Guid.Empty || address.SourceVaultId == address.TargetVaultId)
        {
            throw new ArgumentException("An envelope goes from one vault to another.", nameof(address));
        }
    }

    /// <summary>Writes beside the final name, flushes, and moves over it: a reader sees the old file or the new one.</summary>
    private static void WriteReplacing(string path, ReadOnlySpan<byte> bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + TemporaryExtension;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The write already failed; that is the error worth reporting.
        }
    }
}
