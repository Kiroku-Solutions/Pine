namespace Pine.Core.Security;

/// <summary>
/// The open vault's data key and directory, for the small number of infrastructure components that
/// must encrypt something of their own with it (TD-16).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS, AND WHY IT IS A WIDENING WORTH BEING HONEST ABOUT. <see cref="IVaultLockState"/>
/// was introduced for TD-15 to give <c>Pine.Infrastructure</c> exactly one fact about the session --
/// whether a vault is open -- without inverting the layering, and its doc comment described that
/// narrowness as a feature. This interface deliberately breaks that narrowness. TD-16 requires the
/// ML categoriser, which lives in <c>Pine.Infrastructure</c>, to write a model file encrypted under
/// the vault's data key; the key lives on <c>IVaultSession</c> in <c>Pine.Application</c>, which
/// infrastructure cannot reference. Something had to give, and the alternatives were worse.
/// </para>
/// <para>
/// THE ALTERNATIVE THAT LOOKS RIGHT AND IS NOT. The composition roots already bridge this gap for
/// <c>PineDbContext</c> with a factory lambda that reads the session and hands the key to a scoped
/// object. That pattern cannot serve the categoriser: an ML.NET prediction engine is expensive to
/// build and must be a singleton, so it outlives every scope, and a key captured at construction
/// would be the key of whichever vault happened to be open when the container was built -- which,
/// at container-build time, is none.
/// </para>
/// <para>
/// MEMBERS WERE MOVED HERE, NOT ADDED. <see cref="Key"/> and <see cref="DbPath"/> were declared on
/// <c>IVaultSession</c> and are relocated to this interface unchanged, under the same names and
/// with the same contracts; <c>IVaultSession</c> extends this interface and no longer declares
/// them. Nothing that implemented or consumed those members had to change. That is the whole point
/// of doing it this way: the widening is visible in the type graph rather than smuggled in as a new
/// capability nobody reviewed.
/// </para>
/// <para>
/// WHAT THIS DOES NOT GRANT. A password. <c>IVaultKeyService</c> remains the only place a password
/// and a data key are ever held together, and nothing here changes that.
/// </para>
/// </remarks>
public interface IVaultDataKeyAccessor : IVaultLockState
{
    /// <summary>
    /// The vault encryption key, or <c>null</c> when locked.
    /// </summary>
    /// <remarks>
    /// The returned array is the session's own buffer, not a copy. Locking zeroes it, so a caller
    /// that captures this reference and reads it AFTER a lock reads zeroes -- which fails closed
    /// rather than leaking anything. Callers should use it immediately and never cache it.
    /// </remarks>
    byte[]? Key { get; }

    /// <summary>The directory of the open vault, or <c>null</c> when locked.</summary>
    string? DbPath { get; }
}
