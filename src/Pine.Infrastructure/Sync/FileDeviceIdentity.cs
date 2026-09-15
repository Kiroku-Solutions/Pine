using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Pine.Core.Sync;

namespace Pine.Infrastructure.Sync;

/// <summary>
/// This installation's device id, kept in a small file in Pine's data directory (TD-144).
/// </summary>
/// <remarks>
/// <para>
/// CREATED ONCE, READ FOR EVER, AND NEVER SILENTLY REPLACED. The first read that finds no file writes a new
/// id; every later read returns it. A file that exists but does not hold a valid id is refused rather than
/// overwritten: regenerating would give every row this installation writes afterwards a second identity, and
/// a merge would treat one device as two — the failure this id exists to prevent, caused by the thing meant
/// to prevent it. The refusal names the file, so a person can delete it deliberately.
/// </para>
/// <para>
/// WRITTEN TO A TEMPORARY FILE AND MOVED INTO PLACE WITHOUT OVERWRITING, so two processes starting at once
/// cannot both win: the loser's move fails, and it reads the winner's id instead of keeping its own.
/// </para>
/// </remarks>
public sealed class FileDeviceIdentity : IDeviceIdentity
{
    /// <summary>The file's name inside the data directory.</summary>
    public const string FileName = "device-id";

    private readonly string _path;
    private readonly Lazy<Guid> _deviceId;

    /// <summary>Creates the identity for a data directory.</summary>
    /// <param name="dataDirectory">Pine's per-user data directory.</param>
    public FileDeviceIdentity(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _path = Path.Combine(dataDirectory, FileName);
        // PublicationOnly: a refusal or an I/O error is NOT cached. With the default mode the first exception was
        // remembered for the life of this singleton, so deleting a damaged file as the refusal instructs still
        // failed every vault operation until the app restarted (independent review of plan item 3.1).
        _deviceId = new Lazy<Guid>(LoadOrCreate, LazyThreadSafetyMode.PublicationOnly);
    }

    /// <inheritdoc />
    public Guid DeviceId => _deviceId.Value;

    private Guid LoadOrCreate()
    {
        if (File.Exists(_path))
        {
            return Read();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var candidate = Guid.NewGuid();
        var temporary = _path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";

        // Flushed to disk before the move, so a power cut cannot leave a zero-length id in place — which would be
        // refused on every start until someone found and deleted a hidden file.
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var bytes = Encoding.ASCII.GetBytes(candidate.ToString("D", CultureInfo.InvariantCulture));
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
        try
        {
            File.Move(temporary, _path, overwrite: false);
            return candidate;
        }
        catch (IOException) when (File.Exists(_path))
        {
            // Another process created it between our check and our move. Theirs is the id.
            File.Delete(temporary);
            return Read();
        }
    }

    private Guid Read()
    {
        var text = File.ReadAllText(_path).Trim();

        return Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty
            ? id
            : throw new InvalidDataException(
                $"The device id file '{_path}' does not hold a valid id. It is not replaced automatically, because a new id would make this installation look like a second device to any vault it has written; delete the file to start a new identity.");
    }
}
