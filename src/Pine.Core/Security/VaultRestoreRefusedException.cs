using System;
using Pine.Core.Common;

namespace Pine.Core.Security;

/// <summary>
/// Thrown when a restore was refused BEFORE anything was replaced, and the vault is untouched.
/// </summary>
/// <remarks>
/// <para>
/// EVERY CONSTRUCTION SITE OF THIS EXCEPTION IS A REFUSAL, NOT A FAILURE, and the distinction is the
/// point. A restore replaces the user's entire ledger with an older copy of itself. The dangerous
/// outcome is not "the restore did not work" — it is "the restore half-worked", or "the restore
/// worked and the file was not what the user thought it was". So every check that could produce
/// either runs first, and each one that says no throws this, with the vault exactly as it was.
/// </para>
/// <para>
/// IT IMPLEMENTS <see cref="IUserFacingFailure"/> BECAUSE THE REASON IS THE USER'S BUSINESS. "This
/// backup was made by a newer version of Pine" is not an internal detail: it is the one fact that
/// tells them what to do next. The sentences are written at each site under the rule
/// <see cref="IUserFacingFailure"/> states — no path, no exception type, no schema identifier — and
/// the engineer's version, with those details, goes to <see cref="Exception.Message"/> and the log.
/// </para>
/// </remarks>
public sealed class VaultRestoreRefusedException : Exception, IUserFacingFailure
{
    public VaultRestoreRefusedException(string message, string userFacingMessage, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userFacingMessage);
        UserFacingMessage = userFacingMessage;
    }

    /// <inheritdoc />
    public string UserFacingMessage { get; }
}
