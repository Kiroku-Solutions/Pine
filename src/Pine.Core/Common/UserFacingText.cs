namespace Pine.Core.Common;

/// <summary>
/// Every <see cref="UserFacingMessage"/> <c>Pine.Core</c> and <c>Pine.Infrastructure</c> can raise,
/// one factory per key (TD-139).
/// </summary>
/// <remarks>
/// <para>
/// ONE METHOD PER KEY, AND ITS PARAMETERS ARE THE TEMPLATE'S PLACEHOLDERS. The wording lives in the
/// presentation layer's <c>en.json</c> and <c>es.json</c>; this class is the only place below that
/// layer that knows the keys. <c>UserFacingCatalogTests</c> reads it by reflection and holds every
/// method to three things: its key exists in every shipped table, each table's template has exactly
/// as many placeholders as the method has parameters, and no other file below the UI constructs a
/// <see cref="UserFacingMessage"/> itself.
/// </para>
/// <para>
/// <c>Pine.Application</c>'s validation sentences are in <c>ValidationText</c>, beside the validators
/// that raise them, under the same rules.
/// </para>
/// </remarks>
public static class UserFacingText
{
    /// <summary>Any command refused because the vault locked underneath it (TD-11).</summary>
    public static UserFacingMessage VaultLocked() => new("Failure.VaultLocked");

    /// <summary>A write refused because the row changed after it was read (TD-17).</summary>
    public static UserFacingMessage ConcurrencyConflict() => new("Failure.ConcurrencyConflict");

    /// <summary>A stored receipt whose bytes no longer match their recorded fingerprint.</summary>
    public static UserFacingMessage AttachmentCorrupted() => new("Failure.AttachmentCorrupted");

    /// <summary>Receipt scanning asked for on a head that has no scanner.</summary>
    public static UserFacingMessage ReceiptScanningUnavailable() => new("Failure.ReceiptScanningUnavailable");

    /// <summary>An import file whose extension no importer reads.</summary>
    public static UserFacingMessage ImportFormatUnsupported() => new("Failure.ImportFormatUnsupported");

    /// <summary>A restore source outside this vault's backups directory.</summary>
    public static UserFacingMessage RestoreNotAVaultBackup() => new("Failure.Restore.NotThisVaultsBackup");

    /// <summary>A restore source that was deleted after it was listed.</summary>
    public static UserFacingMessage RestoreBackupMissing() => new("Failure.Restore.BackupMissing");

    /// <summary>A restore into a vault whose database is not on disk.</summary>
    public static UserFacingMessage RestoreVaultMissing() => new("Failure.Restore.VaultMissing");

    /// <summary>A backup carrying migrations this build does not know.</summary>
    public static UserFacingMessage RestoreFromNewerVersion() => new("Failure.Restore.FromNewerVersion");

    /// <summary>A backup that could not be put in place of the live database.</summary>
    public static UserFacingMessage RestoreCouldNotReplace() => new("Failure.Restore.CouldNotReplace");

    /// <summary>A file that opens but carries no migration history.</summary>
    public static UserFacingMessage RestoreNotAPineBackup() => new("Failure.Restore.NotAPineBackup");

    /// <summary>A backup that could not be opened with this vault's key.</summary>
    public static UserFacingMessage RestoreUnreadable() => new("Failure.Restore.Unreadable");

    /// <summary>The receipt detector was asked to analyse a frame before its model loaded.</summary>
    public static UserFacingMessage ScanNotReady() => new("Scan.Guidance.NotReady");

    /// <summary>A camera frame that could not be decoded as an image.</summary>
    public static UserFacingMessage ScanUnreadableFrame() => new("Scan.Guidance.UnreadableFrame");

    /// <summary>No receipt found in the frame yet.</summary>
    public static UserFacingMessage ScanSearching() => new("Scan.Guidance.Searching");

    /// <summary>The loaded detection model is not a segmentation model.</summary>
    public static UserFacingMessage ScanWrongModel() => new("Scan.Guidance.WrongModel");

    /// <summary>A receipt was seen but its outline could not be traced.</summary>
    public static UserFacingMessage ScanCannotOutline() => new("Scan.Guidance.CannotOutline");

    /// <summary>The receipt's outline is too irregular to crop.</summary>
    public static UserFacingMessage ScanIrregularOutline() => new("Scan.Guidance.IrregularOutline");

    /// <summary>The receipt touches the frame's edges.</summary>
    public static UserFacingMessage ScanMoveBack() => new("Scan.Guidance.MoveBack");

    /// <summary>The outline is nearly square, which a receipt is not.</summary>
    public static UserFacingMessage ScanNotRectangular() => new("Scan.Guidance.NotRectangular");

    /// <summary>The receipt fills too little of the frame.</summary>
    public static UserFacingMessage ScanMoveCloser() => new("Scan.Guidance.MoveCloser");

    /// <summary>A frame that passed every filter.</summary>
    public static UserFacingMessage ScanCaptured() => new("Scan.Guidance.Captured");

    /// <summary>A sync conflict resolution naming a conflict that is not recorded (post-audit plan item 3.1c).</summary>
    public static UserFacingMessage SyncConflictNotFound() => new("Failure.SyncConflict.NotFound");

    /// <summary>A sync conflict that was resolved already, here or by a later change from the other device.</summary>
    public static UserFacingMessage SyncConflictAlreadyResolved() => new("Failure.SyncConflict.AlreadyResolved");

    /// <summary>Taking the other device's setting would grant a consent, and that was not confirmed (§7).</summary>
    public static UserFacingMessage SyncConflictConsentNotConfirmed() => new("Failure.SyncConflict.ConsentNotConfirmed");

    /// <summary>The other device's version points at something this device does not have.</summary>
    public static UserFacingMessage SyncConflictParentMissing() => new("Failure.SyncConflict.ParentMissing");

    /// <summary>The other device deleted something items on this device still point at.</summary>
    public static UserFacingMessage SyncConflictStillPointedAt() => new("Failure.SyncConflict.StillPointedAt");

    /// <summary>The other device's version matches more than one item here.</summary>
    public static UserFacingMessage SyncConflictAmbiguous() => new("Failure.SyncConflict.Ambiguous");

    /// <summary>An item like the other device's already exists here.</summary>
    public static UserFacingMessage SyncConflictAlreadyExistsHere() => new("Failure.SyncConflict.AlreadyExistsHere");

    /// <summary>The other device's version was recorded before this version of Pine changed how it stores that item.</summary>
    public static UserFacingMessage SyncConflictSchemaChanged() => new("Failure.SyncConflict.SchemaChanged");

    /// <summary>No pairing offer where the user said there was one (post-audit plan item 3.1e).</summary>
    public static UserFacingMessage SyncPairingOfferNotFound() => new("Failure.SyncPairing.OfferNotFound");

    /// <summary>The file chosen is not a pairing offer.</summary>
    public static UserFacingMessage SyncPairingMalformed() => new("Failure.SyncPairing.Malformed");

    /// <summary>A pairing offer written by a version of Pine this one does not read.</summary>
    public static UserFacingMessage SyncPairingUnsupportedVersion() => new("Failure.SyncPairing.UnsupportedVersion");

    /// <summary>What was typed is not a pairing code at all.</summary>
    public static UserFacingMessage SyncPairingCodeFormat() => new("Failure.SyncPairing.CodeFormat");

    /// <summary>The code is wrong, or the offer was altered — one sentence for both, on purpose.</summary>
    public static UserFacingMessage SyncPairingNotAuthentic() => new("Failure.SyncPairing.NotAuthentic");

    /// <summary>The pairing offer's short life is over.</summary>
    public static UserFacingMessage SyncPairingExpired() => new("Failure.SyncPairing.Expired");

    /// <summary>The pairing offer was made by this vault, and pairing is between two.</summary>
    public static UserFacingMessage SyncPairingSameVault() => new("Failure.SyncPairing.SameVault");

    /// <summary>The offer opened but does not hold the key it says it does.</summary>
    public static UserFacingMessage SyncPairingPayloadMismatch() => new("Failure.SyncPairing.PayloadMismatch");

    /// <summary>This vault has already been paired with that offer.</summary>
    public static UserFacingMessage SyncPairingReplayed() => new("Failure.SyncPairing.Replayed");

    /// <summary>This vault already holds a different sync key, which is never replaced silently.</summary>
    public static UserFacingMessage SyncPairingDifferentKey() => new("Failure.SyncPairing.DifferentKey");

    /// <summary>A sync run on a vault that holds no sync key (post-audit plan item 3.1f).</summary>
    public static UserFacingMessage SyncRunNoKey() => new("Failure.SyncRun.NoKey");

    /// <summary>A sync run against a folder that is not there or cannot be used — §9's offline state.</summary>
    public static UserFacingMessage SyncRunFolderUnavailable() => new("Failure.SyncRun.FolderUnavailable");
}
