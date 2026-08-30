using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Pine.Core.Ai;
using Pine.Core.Security;

namespace Pine.Infrastructure.Ai.Categorisation;

/// <summary>
/// Stores the categorisation model inside the open vault's directory, encrypted under a key derived
/// from that vault's data key (TD-16).
/// </summary>
/// <remarks>
/// <para>
/// THE THREE DEFECTS THIS CLASS EXISTS TO MAKE UNREACHABLE, restated from the port's docs because
/// this is where they are actually closed:
/// </para>
/// <list type="number">
///   <item><description>The old path was relative, so it resolved against the process working
///   directory -- a different file depending on how the app was launched. Here it is always built
///   from <c>IVaultDataKeyAccessor.DbPath</c>, which is an absolute vault directory.</description></item>
///   <item><description>The old file was plaintext. Every byte written here goes through
///   <see cref="IVaultFileCipher"/> first, and this class has no code path that writes anything
///   else.</description></item>
///   <item><description>The old file was shared by every vault. This one is inside the vault, and
///   even if it were copied between vaults it would not open: the key is derived from the data key
///   of the vault it was sealed in.</description></item>
/// </list>
/// <para>
/// WHY THE FINGERPRINT IS THE DIRECTORY. It has to be something that changes when the vault
/// changes, is available without touching the disk (it is read on every prediction), and carries no
/// secret. The vault directory is all three. It is deliberately NOT the vault id from the header,
/// which would mean a file read per call for no additional discrimination.
/// </para>
/// </remarks>
public sealed partial class VaultCategorizerModelStore : ICategorizerModelStore
{
    /// <summary>Sub-directory of the vault holding derived AI artefacts.</summary>
    /// <remarks>
    /// A sub-directory rather than a loose file, so that the vault directory keeps a readable
    /// inventory as Phase 3 adds more derived artefacts, and so that the backup logic's file
    /// globbing has something to include or exclude as a unit.
    /// </remarks>
    public const string ModelDirectoryName = "ai";

    /// <summary>File name of the sealed model. No extension implying a readable format: it is not one.</summary>
    public const string ModelFileName = "categorizer.model";

    private readonly IVaultDataKeyAccessor _vault;
    private readonly IVaultFileCipher _cipher;
    private readonly ILogger<VaultCategorizerModelStore> _logger;

    /// <summary>Creates the store.</summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public VaultCategorizerModelStore(
        IVaultDataKeyAccessor vault,
        IVaultFileCipher cipher,
        ILogger<VaultCategorizerModelStore> logger)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(logger);

        _vault = vault;
        _cipher = cipher;
        _logger = logger;
    }

    /// <inheritdoc />
    public string? VaultFingerprint => _vault.IsUnlocked ? _vault.DbPath : null;

    /// <inheritdoc />
    public byte[]? TryRead()
    {
        var path = RequireModelPath();

        byte[] sealedBlob;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            sealedBlob = File.ReadAllBytes(path);
        }
        catch (IOException ex)
        {
            // A model we cannot read is a model we retrain. Logged at warning because it is not
            // normal, but not raised, because nothing the user is doing should fail over it.
            LogReadFailed(_logger, ex);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogReadDenied(_logger, ex);
            return null;
        }

        try
        {
            return _cipher.Decrypt(sealedBlob, VaultFilePurposes.CategorizerModel);
        }
        catch (VaultFileCipherException)
        {
            // DELIBERATELY SWALLOWED, AND THIS IS THE ONE PLACE IT IS ACCEPTABLE. The model is
            // derived data reproducible from the ledger, so discarding it costs one training cycle.
            // The alternative -- surfacing it -- would let a single corrupt cache file block a user
            // from opening their own vault. The exception carries no detail to log anyway, by
            // design, so nothing diagnostic is lost by not rethrowing it.
            LogAuthenticationFailed(_logger);
            return null;
        }
    }

    /// <inheritdoc />
    public void Write(ReadOnlySpan<byte> model)
    {
        if (model.IsEmpty)
        {
            throw new ArgumentException("Refusing to store an empty categorisation model.", nameof(model));
        }

        var path = RequireModelPath();
        var sealedBlob = _cipher.Encrypt(model, VaultFilePurposes.CategorizerModel);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // WRITE-TEMP-THEN-MOVE, because File.WriteAllBytes over an existing model is a torn write
        // waiting to happen: a crash halfway leaves a truncated file that the next start cannot
        // authenticate, so a good model is destroyed by a failed attempt to replace it. With the
        // move, a reader sees the old model or the new one and never a mixture. The temp file sits
        // in the same directory so the move stays on one volume and therefore stays atomic.
        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, sealedBlob);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemporary(temporaryPath);
            throw;
        }
    }

    private void TryDeleteTemporary(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException ex)
        {
            // Best effort. A stale .tmp is litter, not a fault, and it is overwritten next time.
            LogTemporaryCleanupFailed(_logger, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogTemporaryCleanupDenied(_logger, ex);
        }
    }

    private string RequireModelPath()
    {
        var directory = _vault.IsUnlocked ? _vault.DbPath : null;
        if (string.IsNullOrEmpty(directory))
        {
            throw new VaultLockedException("The vault is locked, so the categorisation model cannot be accessed.");
        }

        return Path.Combine(directory, ModelDirectoryName, ModelFileName);
    }

    // The source-generated LoggerMessage delegates. CA1848 is an ERROR in this repository, so every
    // record goes through one of these; the generator is also what stamps the stable, dotted,
    // snake_case EventName that 30-Observability §4.1 requires in the "event" field.
    //
    // NOTE WHAT IS ABSENT FROM EVERY MESSAGE BELOW: the model path, the vault directory, and any
    // byte of the model. A log line about a failed decryption must not become the thing that tells
    // a reader of the log where the encrypted file is.

    [LoggerMessage(
        EventId = 1,
        EventName = "ai.model_store.read_failed",
        Level = LogLevel.Warning,
        Message = "Could not read the stored categorisation model; it will be retrained.")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = "ai.model_store.read_denied",
        Level = LogLevel.Warning,
        Message = "Access denied reading the stored categorisation model; it will be retrained.")]
    private static partial void LogReadDenied(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3,
        EventName = "ai.model_store.authentication_failed",
        Level = LogLevel.Warning,
        Message = "The stored categorisation model failed authentication and was discarded; it will be retrained.")]
    private static partial void LogAuthenticationFailed(ILogger logger);

    [LoggerMessage(
        EventId = 4,
        EventName = "ai.model_store.temp_cleanup_failed",
        Level = LogLevel.Debug,
        Message = "Could not remove the temporary categorisation model file.")]
    private static partial void LogTemporaryCleanupFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5,
        EventName = "ai.model_store.temp_cleanup_denied",
        Level = LogLevel.Debug,
        Message = "Access denied removing the temporary categorisation model file.")]
    private static partial void LogTemporaryCleanupDenied(ILogger logger, Exception exception);
}
