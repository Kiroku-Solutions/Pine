using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pine.Core.Diagnostics;
using Pine.Application.Security;
using Pine.Core.Security;
using Pine.Infrastructure.Data;
using Pine.Infrastructure.Diagnostics;
using Xunit;

namespace Pine.Tests.Unit.Diagnostics;

/// <summary>
/// Covers the TD-15 database probe against a real, file-backed, SQLCipher-encrypted vault resolved
/// out of a real container -- because the two things most likely to be wrong about this type are not
/// things a mock can be wrong about.
/// </summary>
/// <remarks>
/// <para>
/// WHY A REAL <see cref="ServiceCollection"/> AND NOT A SUBSTITUTED SCOPE FACTORY. The probe's whole
/// design exists to survive one specific composition: a SINGLETON probe consumed by a TRANSIENT view
/// model resolved from the ROOT provider, over a SCOPED <c>PineDbContext</c> whose factory throws
/// while the vault is locked. Every one of Pine's five roots runs with <c>ValidateScopes</c> on
/// (TD-01), so the failure mode being guarded against is a container exception, and a fake scope
/// factory cannot throw it. These tests therefore build a provider with
/// <c>ValidateScopes = true</c>, register the context exactly as <c>Program.ConfigureServices</c>
/// does, and resolve the probe from the root -- which is what the view model does.
/// </para>
/// <para>
/// WHY A REAL DATABASE. "Healthy" and "PendingMigrations" are answers EF Core gives after reading
/// <c>__EFMigrationsHistory</c> out of an encrypted file. Substituting that away would leave the
/// tests asserting that a stub returns what it was told to, which is the exact failure TD-15 was
/// filed about: a screen confidently reporting a value nobody measured.
/// </para>
/// </remarks>
public class PineDatabaseHealthProbeTests
{
    /// <summary>A real, file-based, SQLCipher-encrypted vault in a throwaway temp directory.</summary>
    private sealed class TempVault : IDisposable
    {
        static TempVault()
        {
            // SQLCipher provider registration; must happen before any SQLite usage.
            SQLitePCL.Batteries_V2.Init();
        }

        public TempVault()
        {
            VaultDirectory = Path.Combine(
                Path.GetTempPath(),
                "PineDatabaseHealthProbeTests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(VaultDirectory);
            Key = RandomNumberGenerator.GetBytes(32);
        }

        /// <summary>The vault directory -- what <see cref="IVaultSession.DbPath"/> holds in production.</summary>
        public string VaultDirectory { get; }

        public byte[] Key { get; }

        public string DatabasePath => Path.Combine(VaultDirectory, "pine.db");

        public PineDbContext CreateContext() => new(DatabasePath, Key);

        /// <summary>Applies every EF Core migration, producing the schema a healthy vault has.</summary>
        public void Migrate()
        {
            using var context = CreateContext();
            context.Database.Migrate();
        }

        /// <summary>
        /// Produces an encrypted database file with no schema and no migration history -- the state
        /// of a vault whose file exists but which a newer build has not yet upgraded.
        /// </summary>
        public void CreateUnmigratedDatabaseFile()
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Password = Convert.ToHexString(Key),
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var command = connection.CreateCommand();

            // Forces SQLCipher to materialise a real, keyed page-1 header rather than leaving a
            // zero-length file, so the probe's CanConnect question has a genuine database to answer.
            command.CommandText = "PRAGMA user_version = 1;";
            command.ExecuteNonQuery();
        }

        /// <summary>How many migrations the assembly defines: the pending count for an unmigrated file.</summary>
        public int TotalMigrationCount()
        {
            using var context = CreateContext();
            return context.Database.GetMigrations().Count();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();

            try
            {
                if (Directory.Exists(VaultDirectory))
                {
                    Directory.Delete(VaultDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // A leaked temp directory is not worth failing a green test over.
            }
        }
    }

    /// <summary>
    /// A scope factory that fails loudly if it is ever asked for a scope, and counts the asking.
    /// </summary>
    /// <remarks>
    /// This is how "does not touch the database while locked" is PROVEN rather than assumed. It also
    /// throws the exact exception the production factory throws for a locked vault, so a probe that
    /// created a scope anyway would be caught twice: by the count, and by the reading coming back
    /// <see cref="DatabaseHealthStatus.Faulted"/> instead of <see cref="DatabaseHealthStatus.Locked"/>.
    /// </remarks>
    private sealed class FailIfUsedScopeFactory : IServiceScopeFactory
    {
        public int CreateScopeCallCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateScopeCallCount++;
            throw new InvalidOperationException("Cannot access database: Vault is locked.");
        }
    }

    /// <summary>A scratch log directory that deletes itself, used to read back what the probe logged.</summary>
    private sealed class TempLogDirectory : IDisposable
    {
        public TempLogDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PineDatabaseHealthProbeLogs",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public JsonElement[] Records()
            => Directory.Exists(Path)
                ? Directory.GetFiles(Path, "*" + PineLoggingOptions.FileSuffix)
                    .OrderBy(file => file, StringComparer.Ordinal)
                    .SelectMany(File.ReadAllLines)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => JsonDocument.Parse(line).RootElement.Clone())
                    .ToArray()
                : Array.Empty<JsonElement>();

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // A leaked temp directory is not worth failing a green test over.
            }
        }
    }

    private static IVaultSession UnlockedSession(TempVault vault)
    {
        var session = Substitute.For<IVaultSession>();
        session.IsUnlocked.Returns(true);
        session.DbPath.Returns(vault.VaultDirectory);
        session.Key.Returns(vault.Key);
        return session;
    }

    /// <summary>
    /// Builds a container shaped like <c>Program.ConfigureServices</c>: scoped context from a
    /// session-reading factory, singleton probe, and <c>ValidateScopes</c> on.
    /// </summary>
    private static ServiceProvider BuildProvider(
        IVaultSession session,
        ILoggerProvider? loggerProvider = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton(session);

        // The probe depends on IVaultLockState, which IVaultSession extends. AddPineApplication makes
        // this binding in production; the container here is hand-built, so it makes it explicitly.
        services.AddSingleton<IVaultLockState>(session);

        services.AddLogging(builder =>
        {
            if (loggerProvider is not null)
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddProvider(loggerProvider);
            }
        });

        // Verbatim in shape from Program.ConfigureServices: scoped, and it throws while locked.
        services.AddScoped(provider =>
        {
            var vaultSession = provider.GetRequiredService<IVaultSession>();

            if (!vaultSession.IsUnlocked || string.IsNullOrEmpty(vaultSession.DbPath))
            {
                throw new InvalidOperationException("Cannot access database: Vault is locked.");
            }

            return new PineDbContext(Path.Combine(vaultSession.DbPath, "pine.db"), vaultSession.Key!);
        });

        services.AddSingleton<IDatabaseHealthProbe, PineDatabaseHealthProbe>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    // ===== Locked: a normal state, and the database must not be touched =====

    /// <summary>
    /// A locked vault reads <see cref="DatabaseHealthStatus.Locked"/>, and -- the half that actually
    /// matters -- the probe never asks for a scope. With no key in the process the context factory
    /// throws by design, so a probe that created the scope first would report a fault for the
    /// application's ordinary resting state.
    /// </summary>
    [Fact]
    public async Task CheckAsync_ShouldReportLocked_WithoutEverCreatingAScope_WhenTheVaultIsLocked()
    {
        var session = Substitute.For<IVaultSession>();
        session.IsUnlocked.Returns(false);

        var scopeFactory = new FailIfUsedScopeFactory();
        var probe = new PineDatabaseHealthProbe(
            scopeFactory,
            session,
            NullLogger<PineDatabaseHealthProbe>.Instance);

        var health = await probe.CheckAsync(CancellationToken.None);

        health.Should().Be(DatabaseHealth.Locked);
        scopeFactory.CreateScopeCallCount.Should().Be(
            0,
            "a locked vault must be answered without touching the database at all");
    }

    // ===== The measured readings, over a real encrypted database =====

    /// <summary>
    /// A migrated vault reads <see cref="DatabaseHealthStatus.Healthy"/>. The probe is resolved from
    /// the ROOT provider of a container with <c>ValidateScopes</c> on, so this also asserts the
    /// scoped-service-in-a-singleton trap (TD-01) is genuinely avoided: an injected
    /// <c>PineDbContext</c> would make this throw at resolve time.
    /// </summary>
    [Fact]
    public async Task CheckAsync_ShouldReportHealthy_WhenTheVaultDatabaseIsMigrated()
    {
        using var vault = new TempVault();
        vault.Migrate();

        await using var provider = BuildProvider(UnlockedSession(vault));

        var probe = provider.GetRequiredService<IDatabaseHealthProbe>();

        var health = await probe.CheckAsync(CancellationToken.None);

        health.Should().Be(DatabaseHealth.Healthy);
    }

    /// <summary>
    /// A database file that exists but has never been migrated reads
    /// <see cref="DatabaseHealthStatus.PendingMigrations"/>, carrying the real outstanding count --
    /// the number the old panel replaced with the sentence "Migrations are up to date."
    /// </summary>
    /// <remarks>
    /// The expected count comes from <c>GetMigrations()</c> (every migration the assembly defines),
    /// not from <c>GetPendingMigrations()</c>, so the assertion has an independent source rather than
    /// re-asking the question the probe just asked.
    /// </remarks>
    [Fact]
    public async Task CheckAsync_ShouldReportPendingMigrations_WithTheOutstandingCount()
    {
        using var vault = new TempVault();
        vault.CreateUnmigratedDatabaseFile();

        var expected = vault.TotalMigrationCount();
        expected.Should().BePositive("the fixture is meaningless if the assembly defines no migrations");

        await using var provider = BuildProvider(UnlockedSession(vault));

        var health = await provider.GetRequiredService<IDatabaseHealthProbe>()
            .CheckAsync(CancellationToken.None);

        health.Should().Be(new DatabaseHealth(DatabaseHealthStatus.PendingMigrations, expected));
    }

    /// <summary>
    /// An unlocked vault whose database file is not there reads
    /// <see cref="DatabaseHealthStatus.Unreachable"/> -- distinct from
    /// <see cref="DatabaseHealthStatus.Faulted"/>, because "the file is missing" is a diagnosis and
    /// "something threw" is not.
    /// </summary>
    [Fact]
    public async Task CheckAsync_ShouldReportUnreachable_WhenTheDatabaseFileDoesNotExist()
    {
        using var vault = new TempVault();

        await using var provider = BuildProvider(UnlockedSession(vault));

        var health = await provider.GetRequiredService<IDatabaseHealthProbe>()
            .CheckAsync(CancellationToken.None);

        health.Should().Be(DatabaseHealth.Unreachable);
    }

    // ===== Faults: swallowed, logged, and never spoken aloud (TD-23) =====

    /// <summary>
    /// When resolving the context throws, the probe returns
    /// <see cref="DatabaseHealthStatus.Faulted"/> and does not propagate. A diagnostics screen that
    /// crashes because diagnostics are unavailable is worse than one that reports nothing. The
    /// failure is written to the real Pine sink, which is where the detail belongs.
    /// </summary>
    [Fact]
    public async Task CheckAsync_ShouldReportFaulted_AndLogTheFailure_WhenTheContextCannotBeResolved()
    {
        using var logDirectory = new TempLogDirectory();

        var session = Substitute.For<IVaultSession>();
        session.IsUnlocked.Returns(true);
        session.DbPath.Returns((string?)null); // Unlocked but pathless: the factory throws.

        using (var loggerProvider = new PineFileLoggerProvider(new PineLoggingOptions
        {
            Directory = logDirectory.Path,
            MinimumLevel = LogLevel.Trace,
        }))
        {
            await using var provider = BuildProvider(session, loggerProvider);

            var probe = provider.GetRequiredService<IDatabaseHealthProbe>();

            var health = await probe.CheckAsync(CancellationToken.None);

            health.Should().Be(DatabaseHealth.Faulted);
        }

        var record = logDirectory.Records()
            .Should().ContainSingle(entry => entry.GetProperty("event").GetString() == "diagnostics.database_probe_failed")
            .Subject;

        record.GetProperty("level").GetString().Should().Be("error");
        record.TryGetProperty("exception", out _).Should().BeTrue(
            "the detail belongs in the log, where LogRedactor scrubs it");
    }

    /// <summary>
    /// THE TD-23 GUARD. A faulted reading carries a status and nothing else: no message, no path, no
    /// exception text. A SQLite or EF failure message routinely embeds the database file path, and
    /// the whole reason <see cref="DatabaseHealth"/> is a closed set rather than a string is to make
    /// leaking it impossible rather than merely unlikely.
    /// </summary>
    [Fact]
    public async Task CheckAsync_ShouldNotCarryAnyExceptionText_InAFaultedReading()
    {
        const string Secret = @"C:\Users\a-real-person\Documents\MyVault\pine.db";

        var session = Substitute.For<IVaultSession>();
        session.IsUnlocked.Returns(true);

        var services = new ServiceCollection();
        services.AddSingleton(session);
        services.AddSingleton<IVaultLockState>(session);
        services.AddLogging();
        services.AddScoped<PineDbContext>(_ => throw new InvalidOperationException(
            $"unable to open database file: {Secret}"));
        services.AddSingleton<IDatabaseHealthProbe, PineDatabaseHealthProbe>();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        var health = await provider.GetRequiredService<IDatabaseHealthProbe>()
            .CheckAsync(CancellationToken.None);

        health.Status.Should().Be(DatabaseHealthStatus.Faulted);
        health.PendingMigrationCount.Should().Be(0);
        health.ToString().Should().NotContain(Secret);
        health.ToString().Should().NotContain("unable to open database file");

        // The structural half of the guard: adding a message property to the reading would make
        // leaking the exception possible again, so the shape itself is pinned.
        typeof(DatabaseHealth)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Should().BeEquivalentTo(
                new[] { nameof(DatabaseHealth.Status), nameof(DatabaseHealth.PendingMigrationCount) },
                "TD-23: a health reading must never gain a field an exception message could reach");
    }

    // ===== Cancellation is not a fault =====

    /// <summary>
    /// A cancelled refresh throws <see cref="OperationCanceledException"/> rather than reporting
    /// <see cref="DatabaseHealthStatus.Faulted"/>. Navigating away from the diagnostics screen must
    /// not paint the card red -- or write an error record -- for something that was working.
    /// </summary>
    [Fact]
    public async Task CheckAsync_ShouldPropagateCancellation_RatherThanReportingAFault()
    {
        using var vault = new TempVault();
        vault.Migrate();

        await using var provider = BuildProvider(UnlockedSession(vault));

        var probe = provider.GetRequiredService<IDatabaseHealthProbe>();

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = async () => await probe.CheckAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
