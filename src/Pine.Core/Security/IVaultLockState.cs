namespace Pine.Core.Security;

/// <summary>
/// The one fact about the vault session that a component outside the application layer is allowed
/// to know: whether a vault is currently open.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS RATHER THAN <c>IVaultSession</c> ITSELF. <c>IVaultSession</c> lives in
/// <c>Pine.Application</c>, and <c>Pine.Infrastructure</c> deliberately references only
/// <c>Pine.Core</c> -- so an infrastructure component cannot depend on it without inverting the
/// layering. <c>PineDatabaseHealthProbe</c> (TD-15) needs exactly one bit of that interface: it must
/// answer <c>Locked</c> WITHOUT opening a scope, because the scoped <c>PineDbContext</c> factory
/// throws when the vault is closed, and a diagnostics screen must report a locked vault as the
/// normal state it is rather than as a fault.
/// </para>
/// <para>
/// THE ALTERNATIVE WAS EXCEPTION SNIFFING, AND IT WAS REJECTED. The probe could have resolved the
/// context and read the <c>InvalidOperationException</c> the factory throws when locked -- but that
/// exception type is shared with every other misconfiguration, so "locked" and "broken" would have
/// become indistinguishable at exactly the moment the difference matters most.
/// </para>
/// <para>
/// <c>IVaultSession</c> extends this interface, so there is one implementation and no adapter. The
/// binding from this interface to that session is made once, in <c>AddPineApplication</c>.
/// </para>
/// </remarks>
public interface IVaultLockState
{
    /// <summary>Whether a vault is currently open.</summary>
    bool IsUnlocked { get; }
}
