using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pine.Application.Security;
using Pine.Core.Ai;
using Pine.Core.Security;
using Pine.Infrastructure.Ai.Categorisation;
using Pine.Infrastructure.Security;
using Pine.Tests.Integration.Data;

namespace Pine.Tests.Integration.Security;

/// <summary>
/// TD-16 end to end: a real vault session, the real AES-GCM file cipher, the real store, and real
/// files on a real disk.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS WHEN THE UNIT TESTS ALREADY COVER THE PIECES. The unit tests prove the cipher
/// rejects tampering and prove the categoriser drops a foreign vault's engine, but each of them
/// does so against a fake of the other. The specific claim TD-16 makes -- "a model trained in
/// Personal is not readable from Business, and is not readable at all without the vault key" -- is
/// a claim about the COMPOSITION, and it is exactly the kind of claim that survives a green unit
/// suite while being false in the wiring. So this file asserts against the bytes that actually
/// reach the filesystem.
/// </para>
/// <para>
/// The audit's §4.9 evidence line is the thing being falsified here: "ML model written plaintext
/// outside the vault, shared across vaults". Each of those three words gets a test.
/// </para>
/// </remarks>
public class VaultScopedCategorizerModelTests
{
    private const string DistinctivePayee = "TESCO STORES METRO";

    [Fact]
    public async Task AModelTrainedInOneVault_IsNotReadableFromAnother()
    {
        // Arrange - two genuinely separate vaults, each with its own random 32-byte data key. This
        // is "Personal" and "Business": the reason multi-vault exists at all.
        using var personal = new TempVault();
        using var business = new TempVault();

        var groceries = "Groceries";

        // Act - train inside Personal.
        using (var session = OpenSession(personal))
        {
            var categorizer = BuildCategorizer(session);
            await categorizer.TrainModelAsync(BuildHistory(40, groceries));

            (await categorizer.PredictCategoryAsync(DistinctivePayee, 12.50m)).Category
                .Should().Be(groceries, "the vault that trained the model must be able to use it");

            categorizer.Dispose();
        }

        // Assert - Business, with its own key and its own directory, has nothing.
        using (var session = OpenSession(business))
        {
            using var categorizer = BuildCategorizer(session);
            var result = await categorizer.PredictCategoryAsync(DistinctivePayee, 12.50m);

            result.Category.Should().BeEmpty();
            result.ConfidenceScore.Should().Be(0.0);
        }
    }

    [Fact]
    public async Task TheStoredModel_LivesInsideTheVaultDirectoryAndNowhereElse()
    {
        // Arrange
        using var vault = new TempVault();
        using var session = OpenSession(vault);
        using var categorizer = BuildCategorizer(session);

        // Act
        await categorizer.TrainModelAsync(BuildHistory(40));

        // Assert - the pre-TD-16 path was the relative "Assets/Ai/categorizer_model.zip", which
        // resolved against the process working directory, i.e. next to the executable and outside
        // every vault. The file must now be inside the vault the model was trained in.
        var expected = Path.Combine(
            vault.VaultDirectory,
            VaultCategorizerModelStore.ModelDirectoryName,
            VaultCategorizerModelStore.ModelFileName);

        File.Exists(expected).Should().BeTrue();

        // And no stray temp file survived the write-temp-then-move.
        Directory.GetFiles(Path.GetDirectoryName(expected)!)
            .Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public async Task TheStoredModel_IsNotAReadableMlNetArchive()
    {
        // Arrange
        using var vault = new TempVault();
        using var session = OpenSession(vault);
        using var categorizer = BuildCategorizer(session);

        // Act
        await categorizer.TrainModelAsync(BuildHistory(40));

        var path = Path.Combine(
            vault.VaultDirectory,
            VaultCategorizerModelStore.ModelDirectoryName,
            VaultCategorizerModelStore.ModelFileName);
        var onDisk = await File.ReadAllBytesAsync(path);

        // Assert - an ML.NET model is saved as a zip archive, so a plaintext one begins with the
        // local file header "PK\x03\x04". The sealed file must begin with the cipher's own magic
        // instead. This is the assertion that would have failed for every build before TD-16.
        onDisk.Should().StartWith("PINEFC1\0"u8.ToArray());
        onDisk.Take(4).Should().NotEqual(new byte[] { 0x50, 0x4B, 0x03, 0x04 });

        // Nor may the training corpus survive in the clear. A model trained on real payees that
        // stores those payees verbatim would leak them regardless of the container's format.
        IndexOf(onDisk, Encoding.UTF8.GetBytes(DistinctivePayee))
            .Should().Be(-1, "the payees the model was trained on must not appear in the sealed file");
    }

    [Fact]
    public async Task TheStoredModel_DoesNotOpenUnderAnotherVaultsKey()
    {
        // Arrange - train in one vault, then hand the resulting file to a store built over a
        // DIFFERENT key. This is the "attacker copies the file between vault directories" case, and
        // it is the one the shared plaintext model made trivially successful.
        using var source = new TempVault();
        using var target = new TempVault();

        using (var session = OpenSession(source))
        {
            using var categorizer = BuildCategorizer(session);
            await categorizer.TrainModelAsync(BuildHistory(40));
        }

        var sourceModelDirectory = Path.Combine(source.VaultDirectory, VaultCategorizerModelStore.ModelDirectoryName);
        var targetModelDirectory = Path.Combine(target.VaultDirectory, VaultCategorizerModelStore.ModelDirectoryName);
        Directory.CreateDirectory(targetModelDirectory);
        File.Copy(
            Path.Combine(sourceModelDirectory, VaultCategorizerModelStore.ModelFileName),
            Path.Combine(targetModelDirectory, VaultCategorizerModelStore.ModelFileName));

        // Act
        using var targetSession = OpenSession(target);
        var store = BuildStore(targetSession);
        var read = store.TryRead();

        // Assert - null, not an exception. The store treats an unauthenticable model exactly as it
        // treats an absent one, because a model is derived data and the alternative is letting a
        // planted file stop a user opening their own vault.
        read.Should().BeNull();
    }

    [Fact]
    public async Task ASingleBitFlipInTheStoredModel_IsDetectedRatherThanLoaded()
    {
        // Arrange
        using var vault = new TempVault();
        using var session = OpenSession(vault);
        var store = BuildStore(session);

        using (var categorizer = BuildCategorizer(session))
        {
            await categorizer.TrainModelAsync(BuildHistory(40));
        }

        var path = Path.Combine(
            vault.VaultDirectory,
            VaultCategorizerModelStore.ModelDirectoryName,
            VaultCategorizerModelStore.ModelFileName);

        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 0x01;
        await File.WriteAllBytesAsync(path, bytes);

        // Act
        var read = store.TryRead();

        // Assert - AEAD, so one bit is enough. Without authentication this would have decrypted to
        // a subtly corrupt model that ML.NET might well have loaded and predicted from.
        read.Should().BeNull();
    }

    [Fact]
    public async Task LockingTheVault_MakesTheModelInaccessible()
    {
        // Arrange
        using var vault = new TempVault();
        using var session = OpenSession(vault);
        var store = BuildStore(session);

        using (var categorizer = BuildCategorizer(session))
        {
            await categorizer.TrainModelAsync(BuildHistory(40));
        }

        store.TryRead().Should().NotBeNull("the model is readable while the vault is open");

        // Act
        session.Lock();

        // Assert - the session zeroes the key on lock, so there is no key to derive a file key
        // from. Reporting it as a typed exception is what lets the background trainer treat it as
        // an ordinary skipped cycle rather than an error.
        store.Invoking(s => s.TryRead()).Should().Throw<VaultLockedException>();
        store.VaultFingerprint.Should().BeNull();
    }

    private static VaultSession OpenSession(TempVault vault)
    {
        var session = new VaultSession();
        session.Unlock(vault.VaultDirectory, vault.Key);
        return session;
    }

    private static VaultCategorizerModelStore BuildStore(IVaultDataKeyAccessor accessor)
        => new VaultCategorizerModelStore(
            accessor,
            new AesGcmVaultFileCipher(accessor),
            NullLogger<VaultCategorizerModelStore>.Instance);

    private static MlNetCategorizer BuildCategorizer(IVaultDataKeyAccessor accessor)
        => new(BuildStore(accessor), NullLogger<MlNetCategorizer>.Instance);

    /// <summary>
    /// Labels are category NAMES, not identifiers (TD-32) -- see <see cref="TrainingExample"/>.
    /// </summary>
    private static List<TrainingExample> BuildHistory(int rows, string? groceries = null)
    {
        var groceriesLabel = groceries ?? "Groceries";
        const string transport = "Transport";

        return Enumerable.Range(0, rows)
            .Select(i => i % 2 == 0
                ? new TrainingExample($"{DistinctivePayee} {i}", 10m + i, groceriesLabel)
                : new TrainingExample($"UBER TRIP {i}", 20m + i, transport))
            .ToList();
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
