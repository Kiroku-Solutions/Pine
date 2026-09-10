using System;

namespace Pine.Core.Security;

/// <summary>
/// What a completed restore did, so the user can be told it in their own terms and can undo it.
/// </summary>
/// <param name="RestoredFrom">Absolute path of the backup whose contents are now the vault.</param>
/// <param name="BackupCreatedAtUtc">When that backup was taken — the point in time the vault is now at.</param>
/// <param name="SafetyBackupPath">
/// Absolute path of the copy taken of the vault as it stood immediately before the restore. This is
/// the whole reason a restore is not a one-way door: the state that was replaced is still on disk,
/// and restoring THAT file puts everything back. It is never null on success — a restore that could
/// not take this copy first does not proceed.
/// </param>
/// <param name="RestoredAtUtc">When the restore completed.</param>
public sealed record VaultRestoreResult(
    string RestoredFrom,
    DateTimeOffset BackupCreatedAtUtc,
    string SafetyBackupPath,
    DateTimeOffset RestoredAtUtc);
