using System.Threading;

namespace Pine.Infrastructure.Data;

/// <summary>
/// Arms the SQLCipher native provider for this process, exactly once, before anything opens a
/// database.
/// </summary>
/// <remarks>
/// <para>
/// READ THE NEXT PARAGRAPH BEFORE BELIEVING THIS TYPE FIXED A BUG. It did not, and the record of
/// how that was established is the useful part of this file.
/// </para>
/// <para>
/// WHAT WAS OBSERVED. Until iteration 31, <c>SQLitePCL.Batteries_V2.Init()</c> appeared exactly once
/// in <c>src/</c> -- at <c>Pine.Ui.Windows/Program.cs:19</c> -- with a comment saying it "stays in
/// the head and does not move into <c>PineCompositionRoot</c>: it is a process-global native
/// initialisation, not a registration, and each head's provider bundle differs." Three of the four
/// heads never made the call. The clause about differing bundles is FALSE:
/// <c>SQLitePCLRaw.bundle_e_sqlcipher</c> is pinned once in <c>Directory.Packages.props</c>,
/// referenced once by <c>Pine.Infrastructure</c>, and reaches every head through the same
/// <c>ProjectReference</c>.
/// </para>
/// <para>
/// WHAT WAS THEN CLAIMED, AND WAS WRONG. The obvious reading is that this is a shipped defect:
/// <c>Microsoft.EntityFrameworkCore.Sqlite.Core</c> is precisely the variant that omits the
/// auto-arming bundle, so the missing call should mean the first <c>SqliteConnection.Open()</c>
/// throws and no vault opens on macOS, Android or iOS. Iteration 31 wrote that down as the
/// iteration's largest finding. IT IS NOT TRUE. A falsification mutation -- neutering
/// <see cref="EnsureInitialised"/> so it calls nothing -- did not turn any test red, and the probe
/// that followed settled it: a standalone console project referencing only
/// <c>Microsoft.EntityFrameworkCore.Sqlite.Core</c> 10.0.11 and
/// <c>SQLitePCLRaw.bundle_e_sqlcipher</c> 2.1.11, in a clean process with no <c>Init()</c> call
/// anywhere, opens a <c>Password=</c> connection and round-trips a row. The mechanism is in
/// <c>Microsoft.Data.Sqlite.dll</c> itself, which carries the UTF-16 literals
/// <c>"SQLitePCLRaw.batteries_v2"</c>, <c>"Batteries_V2"</c> and <c>"Init"</c>: it finds the bundle
/// BY REFLECTION and initialises it. The explicit call is redundant on this package set.
/// </para>
/// <para>
/// WHY THIS TYPE EXISTS ANYWAY, STATED AS WHAT IT IS. Two reasons, neither of them "it was broken".
/// The first is that one head doing something three do not is a divergence of exactly the kind
/// TD-29 removed everywhere else, and it was justified in a comment whose load-bearing clause was
/// false -- so the asymmetry was being preserved by a wrong reason. The second is a real risk
/// rather than an observed failure, and it is named as such: <c>Microsoft.Data.Sqlite</c> locates
/// the bundle by ASSEMBLY NAME through reflection, and the Android and iOS heads are trimmed AOT
/// targets. Nothing in this codebase holds a static reference to
/// <c>SQLitePCLRaw.batteries_v2</c> without this call, which is the shape a trimmer is entitled to
/// remove. An explicit call is a static reference the trimmer must keep. THIS HAS NOT BEEN
/// VERIFIED ON A DEVICE -- no machine in this project can build those heads (TD-07) -- and it is
/// therefore an argument, not a measurement.
/// </para>
/// <para>
/// It is called from <c>PineCompositionRoot.Build</c>, the one path every head already takes.
/// </para>
/// </remarks>
public static class PineSqliteProvider
{
    private static int _initialised;

    /// <summary>
    /// Whether <see cref="EnsureInitialised"/> has run in this process.
    /// </summary>
    /// <remarks>
    /// A weak witness on its own, and deliberately not dressed up as a strong one: it says this
    /// method ran, not that the provider would otherwise be absent. See the type's remarks.
    /// </remarks>
    public static bool IsInitialised => Volatile.Read(ref _initialised) != 0;

    /// <summary>
    /// Arms the SQLCipher provider. Safe to call any number of times and from any thread; the
    /// second and later calls do nothing.
    /// </summary>
    /// <remarks>
    /// <c>Batteries_V2.Init()</c> is itself idempotent, but the guard is here anyway: it is what
    /// gives <see cref="IsInitialised"/> a meaning, and <c>Build</c> is called many times in a
    /// single test process.
    /// </remarks>
    public static void EnsureInitialised()
    {
        if (Interlocked.Exchange(ref _initialised, 1) != 0)
        {
            return;
        }

        SQLitePCL.Batteries_V2.Init();
    }
}
