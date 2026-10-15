using System;
using System.Globalization;
using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Pine.Infrastructure.Data;
using Pine.Ui.Avalonia.Composition;
using Xunit;

namespace Pine.Tests.Unit.Composition;

/// <summary>
/// The SQLCipher provider is armed in one place for every head, and this file is careful about what
/// that does and does not prove.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THESE TESTS DO NOT PIN. Iteration 31 found that <c>SQLitePCL.Batteries_V2.Init()</c> was
/// called by one head of four and concluded that the other three could not open a vault, on the
/// reasoning that <c>Microsoft.EntityFrameworkCore.Sqlite.Core</c> omits the auto-arming bundle.
/// That conclusion was WRONG and the falsification pass is what caught it: neutering
/// <see cref="PineSqliteProvider.EnsureInitialised"/> so it calls nothing left every test in this
/// file green. A clean-process probe then settled it -- <c>Microsoft.Data.Sqlite</c> carries the
/// literals <c>"SQLitePCLRaw.batteries_v2"</c> and <c>"Batteries_V2"</c> and finds the bundle by
/// reflection, so a keyed connection opens with no explicit call anywhere.
/// </para>
/// <para>
/// THE SURVIVING MUTATION IS LEFT ALIVE ON PURPOSE. Killing it would mean asserting that the
/// explicit call is REQUIRED, which is false on this package set, and the only way to write that
/// test would be to assert something the runtime does not do. The reason the call exists is a
/// trimming risk on the two AOT heads (see <see cref="PineSqliteProvider"/>), and no test in a
/// <c>net10.0</c> unit project can observe a trimmed Android build. Recording the gap is the honest
/// move; a test that passed for a reason nobody checked is how this iteration got the finding wrong
/// in the first place.
/// </para>
/// <para>
/// WHAT THEY DO PIN. That the provider answering a keyed connection in this process is SQLCipher
/// and not some other SQLite; that the initialiser is idempotent, because <c>Build</c> runs many
/// times per test process; and that exactly one place in the tree makes the call.
/// </para>
/// </remarks>
public class SqliteProviderInitialisationTests
{
    /// <summary>
    /// The provider answering a keyed connection in this process is SQLCipher.
    /// </summary>
    /// <remarks>
    /// A <c>Password=</c> connection string reaches SQLCipher's <c>PRAGMA key</c>, which only
    /// exists in the e_sqlcipher build, so a connection that opens, takes a key and round-trips a
    /// row proves the provider is that one and not some other SQLite. It does NOT prove that
    /// <see cref="PineSqliteProvider.EnsureInitialised"/> is what armed it -- see this class's
    /// remarks; that claim was tested and is false.
    /// </remarks>
    [Fact]
    public void AKeyedSqlCipherConnection_Opens_AndRoundTripsARow()
    {
        PineSqliteProvider.EnsureInitialised();
        PineSqliteProvider.IsInitialised.Should().BeTrue();

        var path = Path.Combine(Path.GetTempPath(), $"pine-sqlcipher-{Guid.NewGuid():N}.db");

        // Declared out here because the finally clears the pool for THIS connection string.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = Convert.ToHexString(new byte[32]),
        }.ToString();

        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE t (v INTEGER NOT NULL); INSERT INTO t (v) VALUES (7);";
            create.ExecuteNonQuery();

            using var read = connection.CreateCommand();
            read.CommandText = "SELECT v FROM t;";
            Convert.ToInt64(read.ExecuteScalar(), CultureInfo.InvariantCulture).Should().Be(7L,
                "a keyed connection that opens and round-trips a row proves the SQLCipher provider is armed");
        }
        finally
        {
            // This file's pool only: ClearAllPools would reach into every test class running in
            // parallel, which is TD-140's mechanism.
            using (var forThisFile = new SqliteConnection(connectionString))
            {
                SqliteConnection.ClearPool(forThisFile);
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// Calling it twice is not an error, because <c>Build</c> is called more than once per process
    /// in this very suite.
    /// </summary>
    [Fact]
    public void EnsureInitialised_ShouldBeIdempotent()
    {
        var act = () =>
        {
            PineSqliteProvider.EnsureInitialised();
            PineSqliteProvider.EnsureInitialised();
            PineSqliteProvider.EnsureInitialised();
        };

        act.Should().NotThrow();
        PineSqliteProvider.IsInitialised.Should().BeTrue();
    }

    /// <summary>
    /// The shared composition root arms the provider, and no head does it privately any more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A SOURCE SCAN, AND IT IS NAMED AS ONE. The behavioural test above cannot tell WHO armed the
    /// provider, and on this package set the answer is "the ADO.NET provider did, by reflection,
    /// before anyone asked". There is no seam to observe that would not be a seam invented for the
    /// test. So this reads the files off disk, exactly as
    /// <c>EveryHead_ShouldBuildItsContainer_ThroughTheSharedRootAndNothingElse</c> does, and for
    /// the same reason: it is the strongest instrument available and it catches the realistic
    /// regression, which is someone moving the call back into a head "because it is native".
    /// </para>
    /// <para>
    /// The head half matters as much as the root half. A duplicate call left in
    /// <c>Pine.Ui.Windows</c> would be harmless at run time -- everything here is harmless at run
    /// time -- and would quietly restore the belief that this is a per-head concern, which is the
    /// belief that kept a false comment alive for thirty iterations.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSharedRoot_ShouldArmTheProvider_AndNoHeadShould()
    {
        var root = ReadFromRepository("src/Pine.Ui.Avalonia/Composition/PineCompositionRoot.cs");
        root.Should().Contain("PineSqliteProvider.EnsureInitialised();",
            "Build() is the one path every head takes, which is why the call lives there");

        foreach (var head in new[]
                 {
                     "src/Pine.Ui.Windows/Program.cs",
                     "src/Pine.Ui.Macos/Program.cs",
                     "src/Pine.Ui.Android/Application.cs",
                     "src/Pine.Ui.Ios/AppDelegate.cs",
                 })
        {
            ReadFromRepository(head).Should().NotContain("Batteries_V2",
                $"'{head}' must not arm the SQLite provider privately; one head of four used to, "
                + "defended by a comment claiming the bundle differs per head, and it does not. The "
                + "asymmetry is what this test prevents returning -- not a crash, which there never was");
        }
    }

    private static string ReadFromRepository(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"'{relativePath}' is expected to exist");
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pine.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must be able to find the repository root");
        return directory!.FullName;
    }
}
