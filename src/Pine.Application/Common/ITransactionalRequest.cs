using System.Diagnostics.CodeAnalysis;

namespace Pine.Application.Common;

/// <summary>
/// Marks a MediatR request whose handler must run inside a single database transaction.
/// </summary>
/// <remarks>
/// <para>
/// OPT-IN, NEVER OPT-OUT. A request is transactional only if it explicitly implements this
/// interface; <see cref="Behaviors.TransactionBehavior{TRequest, TResponse}"/> is a no-op for
/// everything else and adds zero overhead to reads.
/// </para>
/// <para>
/// WHY OPT-IN RATHER THAN "TRANSACTIONAL UNLESS OPTED OUT": some commands legitimately cannot run
/// inside an ambient transaction, and an opt-out design makes forgetting the opt-out a runtime
/// failure in production rather than a compile-time omission. The concrete trap in this codebase is
/// <c>ExportVaultBackupCommand</c>: it drives <c>VaultBackupService</c>, which executes
/// <c>VACUUM INTO</c> on the keyed SQLCipher connection. SQLite refuses to run <c>VACUUM</c> from
/// within an open transaction, so wrapping that command in a transaction would break vault backups
/// outright. <c>MigrateVaultCommand</c>-style work is the same shape: <c>Database.MigrateAsync</c>
/// manages its own transactions per migration and must not be nested inside one.
/// </para>
/// <para>
/// So: new write commands that touch more than one repository should implement this marker
/// deliberately. Anything that performs schema work, VACUUM, backup, or other statements SQLite
/// disallows inside a transaction must NOT implement it.
/// </para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1040:Avoid empty interfaces",
    Justification = "Intentional marker interface: it is the opt-in signal TransactionBehavior tests for. " +
                    "Adding a member would force unrelated commands to implement behaviour they do not have.")]
public interface ITransactionalRequest
{
}
