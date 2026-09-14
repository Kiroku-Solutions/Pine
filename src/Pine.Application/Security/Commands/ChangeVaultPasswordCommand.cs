using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Pine.Application.Common;
using Pine.Core.Security;

namespace Pine.Application.Security.Commands;

/// <summary>
/// Re-wraps the open vault's data key under a new master password (TD-06).
/// </summary>
/// <remarks>
/// <para>
/// SAY PLAINLY WHAT THIS DOES NOT DO. It does NOT re-encrypt the database and it does NOT rotate
/// the data key. The envelope has two keys: the AUTH key, derived from the password, which exists
/// only to wrap the data key; and the DATA key, which SQLCipher actually encrypts pages with. This
/// command rotates the auth key and the salt it is derived from, and nothing else. Every encrypted
/// byte on disk stays valid because the key those bytes were written under has not changed.
/// </para>
/// <para>
/// THE SECURITY CONSEQUENCE, STATED RATHER THAN IMPLIED: anyone who already knew the DATA key —
/// not the old password, the KEY — still knows it after this command succeeds, and can still open
/// the database with it. Changing the password defeats someone who knows or can guess the old
/// PASSWORD; it does not defeat someone who extracted the key from memory or from a compromised
/// process. Invalidating a leaked data key requires re-encrypting the whole database under a fresh
/// one, which is a separate, far riskier operation and is deliberately not what this performs.
/// </para>
/// <para>
/// DELIBERATELY NOT <see cref="ITransactionalRequest"/> (TD-01), for the same reason
/// <c>ExportVaultBackupCommand</c> is not. This command writes no row: it rewrites
/// <c>pine.vault.json</c>, a file that lives BESIDE the database and is not covered by any SQLite
/// transaction. Marking it transactional would open and commit an empty write transaction against
/// the vault on every password change, which would protect nothing — a rollback cannot un-write the
/// header file — while advertising an atomicity guarantee the operation does not actually get from
/// that mechanism. Its real atomicity comes from <see cref="IVaultHeaderStore.Write"/>, which is
/// required to be atomic in its own right; see the handler's remarks.
/// </para>
/// </remarks>
/// <param name="CurrentPassword">
/// The password the vault is protected by right now. It is proof of authorisation, not a lookup
/// key: the change is refused unless it actually unwraps the stored data key.
/// </param>
/// <param name="NewPassword">
/// The password the vault should be protected by afterwards. Held to the same entropy floor a new
/// vault is held to; see <see cref="ChangeVaultPasswordCommandValidator"/>.
/// </param>
public sealed record ChangeVaultPasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Unit>;

/// <summary>
/// Handles <see cref="ChangeVaultPasswordCommand"/> by reading the vault header, asking the crypto
/// adapter to re-wrap it, and writing the result back.
/// </summary>
/// <remarks>
/// <para>
/// THE HANDLER NEVER SEES THE DATA KEY, AND THAT IS THE WHOLE DESIGN. Unwrap-then-rewrap happens
/// inside <see cref="IVaultKeyService.ChangePassword"/>. If this handler orchestrated the two steps
/// itself it would necessarily hold the plaintext data key in a local variable, in a layer that has
/// no business seeing one and with nothing to zero it afterwards. Three calls — read, change,
/// write — is the entire body precisely so that no key material passes through the application
/// layer at all.
/// </para>
/// <para>
/// CRASH SAFETY COMES FROM THE ATOMIC WRITE, NOT FROM A TRANSACTION.
/// <see cref="IVaultHeaderStore.Write"/> is contractually atomic: the new header either lands whole
/// or does not land at all. So the only two states a crash can leave behind are "old header, old
/// password works" and "new header, new password works". There is no window in which a half-written
/// header exists, and therefore no window in which the vault becomes unopenable by anyone. It fails
/// CLOSED — a failed password change is an inconvenience; an unopenable vault with no recovery
/// phrase (15-Security) is permanent data loss.
/// </para>
/// <para>
/// THE OPEN SESSION IS LEFT ALONE ON PURPOSE. The data key did not change, so the key
/// <see cref="IVaultSession"/> is holding is still the correct key for the database on disk.
/// Locking the session here would log the user out of a vault they are actively using as a side
/// effect of a settings change, and force them to immediately re-enter the password they just set —
/// a worse experience justified by nothing, because there is no stale secret to discard. The
/// session is untouched.
/// </para>
/// <para>
/// NOTHING IS VERIFIED AGAINST THE DATABASE AFTERWARDS, because there is nothing to verify. A
/// re-key operation would warrant a read-back — you would want proof the database still opens
/// before declaring success. Here the database was never touched and the key that opens it is
/// unchanged, so a test open would only prove that the old data key still works, which was never in
/// doubt. The single thing that could have gone wrong is the header write, and that is the
/// header store's atomicity guarantee, asserted where it belongs rather than re-checked here.
/// </para>
/// </remarks>
public sealed class ChangeVaultPasswordCommandHandler : IRequestHandler<ChangeVaultPasswordCommand, Unit>
{
    private readonly IVaultSession _session;
    private readonly IVaultHeaderStore _headerStore;
    private readonly IVaultKeyService _keyService;

    /// <summary>
    /// Initialises the handler.
    /// </summary>
    /// <param name="session">Tells the handler which vault directory is open, and whether one is.</param>
    /// <param name="headerStore">Reads the current header and writes the re-wrapped one atomically.</param>
    /// <param name="keyService">Performs the unwrap and re-wrap without exposing the data key.</param>
    public ChangeVaultPasswordCommandHandler(
        IVaultSession session,
        IVaultHeaderStore headerStore,
        IVaultKeyService keyService)
    {
        _session = session;
        _headerStore = headerStore;
        _keyService = keyService;
    }

    /// <inheritdoc />
    /// <exception cref="VaultLockedException">No vault is open.</exception>
    /// <exception cref="VaultKeyUnwrapException">
    /// The current password is wrong, or the header has been tampered with. Deliberately allowed to
    /// propagate; see the remarks in the body.
    /// </exception>
    public Task<Unit> Handle(ChangeVaultPasswordCommand request, CancellationToken cancellationToken)
    {
        // DbPath is the vault DIRECTORY (it contains pine.db and pine.vault.json). A locked session
        // has neither a directory nor a key, so there is no vault whose password could be changed --
        // and "change the password of the vault you are not looking at" is not a coherent request.
        if (!_session.IsUnlocked || string.IsNullOrWhiteSpace(_session.DbPath))
        {
            throw new VaultLockedException("Cannot change the vault password while the vault is locked.");
        }

        var vaultDirectory = _session.DbPath;

        var header = _headerStore.Read(vaultDirectory);

        // VaultKeyUnwrapException is NOT caught. It has exactly one meaning here -- the current
        // password did not unwrap the data key, i.e. it was wrong -- and the caller is the layer
        // that knows how to say that to a user. Translating it into a validation error would mean
        // verifying the password inside the validator, which would require unwrapping the data key
        // OUTSIDE this method: precisely the thing IVaultKeyService.ChangePassword exists to avoid.
        var newHeader = _keyService.ChangePassword(request.CurrentPassword, request.NewPassword, header);

        // The commit point. Everything before this is pure computation over a header the store still
        // holds the old copy of; a throw above changes nothing on disk.
        _headerStore.Write(vaultDirectory, newHeader);

        return Task.FromResult(Unit.Value);
    }
}

/// <summary>
/// Rejects a <see cref="ChangeVaultPasswordCommand"/> that must not be attempted, before the
/// handler touches the header file.
/// </summary>
/// <remarks>
/// <para>
/// THIS VALIDATOR CANNOT CHECK THE CURRENT PASSWORD, AND MUST NOT TRY. Confirming it means
/// unwrapping the data key, and the only method that unwraps is the one that also re-wraps. A
/// wrong current password is therefore reported by the handler, as an exception, and everything
/// here is about the shape of the request and the strength of the NEW password — the two things
/// answerable without any key material at all.
/// </para>
/// <para>
/// THE ENTROPY FLOOR IS THE POINT OF THE WHOLE VALIDATOR. <c>SetupViewModel</c> refuses to CREATE a
/// vault below <c>PasswordStrengthPolicy.MinimumEntropyBits</c> (see the arithmetic on that
/// constant). A password change that skipped the same check would be a documented way to walk
/// straight around that gate: create a strong vault, then immediately change to <c>hunter2</c>. A
/// vault must never end up weaker than one it would have been allowed to be created as, so the same
/// policy is applied at the same strictness on this path.
/// </para>
/// <para>
/// NO MESSAGE HERE ECHOES A PASSWORD, OR ANY FRAGMENT OF ONE (TD-24; <c>20-Coding-Standards.md</c>
/// §10: no message may include a secret). Validation errors travel further than the moment they are
/// produced — into <c>RequestValidationException</c>, onto a screen, and into whatever a user
/// screenshots or a log captures. Every message below names a PROPERTY and a RULE, never a value,
/// and the strength refusal reports only the derived verdict, never the candidate. This is also why
/// no bit count is quoted back: it is a measurement OF the secret.
/// </para>
/// <para>
/// NO CONFIRM FIELD IS VALIDATED, because the command has no confirm field. A mistyped new password
/// is not data loss here the way it is at setup — the vault is open, the old password still worked
/// a moment ago, and the user can change it again. Confirmation is a UI affordance and belongs to
/// the view model that collects the two boxes, not to a rule the application layer can enforce over
/// a request that only ever carries one string.
/// </para>
/// </remarks>
public sealed class ChangeVaultPasswordCommandValidator : IRequestValidator<ChangeVaultPasswordCommand>
{
    /// <summary>
    /// Checks that both passwords were supplied, that the new one clears the entropy floor, and
    /// that it is actually different from the current one.
    /// </summary>
    /// <param name="request">The command about to be handled.</param>
    /// <param name="cancellationToken">
    /// Accepted to satisfy the contract and otherwise unused: every rule here is a pure function of
    /// the request, so there is no I/O to cancel.
    /// </param>
    /// <returns>An empty list when the change may proceed, otherwise the errors that stop it.</returns>
    public Task<IReadOnlyList<ValidationError>> ValidateAsync(
        ChangeVaultPasswordCommand request,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();

        // An empty current password cannot possibly unwrap the header -- IVaultKeyService rejects it
        // with ArgumentException before it even derives an auth key. Catching it here turns an
        // argument-validation throw from the crypto adapter into a sentence about a form field.
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            errors.Add(new ValidationError(
                nameof(ChangeVaultPasswordCommand.CurrentPassword),
                "Enter your current vault password.",
                ValidationText.PasswordCurrentRequired()));
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            // Emptiness is reported on its own and the strength rule is skipped: the estimator would
            // report the same fact as PasswordWeakness.Empty, and telling a user their blank field is
            // both blank and weak is noise, not help.
            errors.Add(new ValidationError(
                nameof(ChangeVaultPasswordCommand.NewPassword),
                "Enter a new vault password.",
                ValidationText.PasswordNewRequired()));
        }
        else if (!PasswordStrengthPolicy.Evaluate(request.NewPassword).MeetsFloor)
        {
            // The verdict only. Not the estimate, not the weakness list, not the candidate: each of
            // those describes the secret, and this message is not a place a secret may appear.
            errors.Add(new ValidationError(
                nameof(ChangeVaultPasswordCommand.NewPassword),
                "The new password does not meet Pine's minimum strength for a vault password.",
                ValidationText.PasswordTooWeak()));
        }

        // Ordinal, not culture-aware: two passwords that a culture considers equivalent are still
        // different byte sequences and would derive different auth keys, so a culture-sensitive
        // comparison would reject a change that is in fact a real change. Compared only when both
        // are present, so this never fires as a second complaint about an already-empty field.
        if (!string.IsNullOrWhiteSpace(request.CurrentPassword)
            && !string.IsNullOrWhiteSpace(request.NewPassword)
            && string.Equals(request.CurrentPassword, request.NewPassword, StringComparison.Ordinal))
        {
            // A no-op change is refused rather than quietly performed. Re-wrapping under the same
            // password does rotate the salt, but the user asked to CHANGE their password and did
            // not; reporting success would tell them something untrue about the state of their
            // vault -- notably that an old password they meant to retire no longer opens it.
            errors.Add(new ValidationError(
                nameof(ChangeVaultPasswordCommand.NewPassword),
                "The new password must be different from your current password.",
                ValidationText.PasswordUnchanged()));
        }

        return Task.FromResult<IReadOnlyList<ValidationError>>(errors);
    }
}
