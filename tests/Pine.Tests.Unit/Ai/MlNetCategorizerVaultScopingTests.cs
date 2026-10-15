using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pine.Core.Ai;
using Pine.Core.Security;
using Pine.Infrastructure.Ai.Categorisation;
using Xunit;

namespace Pine.Tests.Unit.Ai;

/// <summary>
/// TD-16: the categoriser must belong to the vault that is open, and must stop belonging to it the
/// moment that vault closes.
/// </summary>
/// <remarks>
/// <para>
/// THESE TESTS DRIVE THE REAL <c>MlNetCategorizer</c> AGAINST A FAKE STORE, NOT A FAKE CIPHER.
/// Whether the bytes are encrypted correctly is <c>VaultFileCipherTests</c>'s question; whether the
/// right bytes are asked for, and whether the wrong vault's model can ever answer, is this file's.
/// Splitting them that way is what keeps these tests fast: a real store would mean real AES and
/// real files for assertions that are about neither.
/// </para>
/// <para>
/// The fake store records every call, which is what lets the "does not re-read on every prediction"
/// test exist at all. That test looks like a performance test and is not one: without the
/// load-attempted flag the categoriser re-reads and re-decrypts the model on EVERY categorised
/// import row, which on a large import is thousands of AES operations against a file that has not
/// changed.
/// </para>
/// </remarks>
public class MlNetCategorizerVaultScopingTests
{
    private const string VaultA = @"C:\vaults\personal";
    private const string VaultB = @"C:\vaults\business";

    [Fact]
    public async Task PredictCategoryAsync_WithNoStoredModel_ReturnsNoCategoryAtZeroConfidence()
    {
        // Arrange
        var store = new FakeCategorizerModelStore { VaultFingerprint = VaultA };
        using var categorizer = NewCategorizer(store);

        // Act
        var result = await categorizer.PredictCategoryAsync("TESCO", 12.50m);

        // Assert - a vault whose user has never categorised anything must not receive a guess. An
        // arbitrary category asserted with any confidence at all is worse than an empty one,
        // because CategorizeTransactionQueryHandler turns a non-empty Guid into a real category on
        // a real transaction.
        result.Category.Should().BeEmpty();
        result.ConfidenceScore.Should().Be(0.0);
    }

    [Fact]
    public async Task PredictCategoryAsync_WhenTheVaultIsLocked_ReturnsNoCategoryAndReadsNothing()
    {
        // Arrange - a locked vault reports a null fingerprint.
        var store = new FakeCategorizerModelStore { VaultFingerprint = null };
        using var categorizer = NewCategorizer(store);

        // Act
        var result = await categorizer.PredictCategoryAsync("TESCO", 12.50m);

        // Assert
        result.Category.Should().BeEmpty();
        store.ReadCount.Should().Be(0, "a locked vault has no key, so there is nothing to decrypt");
    }

    [Fact]
    public async Task TrainModelAsync_WhenTheVaultIsLocked_ThrowsTheTypedLockedException()
    {
        // Arrange
        var store = new FakeCategorizerModelStore { VaultFingerprint = null };
        using var categorizer = NewCategorizer(store);

        // Act
        var act = async () => await categorizer.TrainModelAsync(BuildHistory(20));

        // Assert - typed, not an InvalidOperationException whose message must be sniffed for the
        // word "Vault". That sniffing is TD-23's example, and the background service catches this
        // type precisely so it can treat a locked vault as an ordinary skipped cycle.
        await act.Should().ThrowAsync<VaultLockedException>();
        store.WriteCount.Should().Be(0);
    }

    [Fact]
    public async Task TrainModelAsync_BelowTheRowThreshold_WritesNothing()
    {
        // Arrange
        var store = new FakeCategorizerModelStore { VaultFingerprint = VaultA };
        using var categorizer = NewCategorizer(store);

        // Act
        await categorizer.TrainModelAsync(BuildHistory(MlNetCategorizer.MinimumTrainingRows - 1));

        // Assert - refusing to train is refusing to WRITE. If a sub-threshold run stored a model,
        // the next start would load an overfitted one and predict confidently from nine rows.
        store.WriteCount.Should().Be(0);
    }

    [Fact]
    public async Task TrainModelAsync_ThenPredict_UsesTheModelItJustTrained_OnTheSameInstance()
    {
        // Arrange
        var store = new FakeCategorizerModelStore { VaultFingerprint = VaultA };
        using var categorizer = NewCategorizer(store);
        var groceries = "Groceries";

        // Act - train on a corpus where one distinctive payee maps to one category.
        await categorizer.TrainModelAsync(BuildHistory(40, groceries));
        var result = await categorizer.PredictCategoryAsync("TESCO STORES", 12.50m);

        // Assert - THIS IS THE REGRESSION TEST FOR THE TWO-INSTANCE DEFECT. Before TD-16 the
        // container registered one MlNetCategorizer as ICategorizationTrainer while
        // DefaultLocalAiProvider constructed a second for predictions, so training rebuilt an
        // engine that no prediction ever went through and this assertion would have been
        // unreachable through the composed graph. It passes here only because one object both
        // trains and predicts.
        store.WriteCount.Should().Be(1);
        result.Category.Should().Be(groceries);
    }

    [Fact]
    public async Task TrainModelAsync_WhenTheVaultChangesMidRun_DiscardsTheModelInsteadOfFilingIt()
    {
        // Arrange - the store reports vault A when training starts and vault B by the time the
        // trained model is ready to be written. That is exactly what an idle auto-lock followed by
        // a different unlock looks like from in here, and TD-11's watchdog runs on a timer thread
        // that does not wait for a training run.
        var store = new FakeCategorizerModelStore { VaultFingerprint = VaultA };
        store.OnFingerprintRead = readCount =>
        {
            if (readCount >= 2)
            {
                store.VaultFingerprint = VaultB;
            }
        };

        using var categorizer = NewCategorizer(store);

        // Act
        await categorizer.TrainModelAsync(BuildHistory(40));

        // Assert - writing here would seal one user's spending patterns into another user's vault.
        // Losing the run costs a single five-minute cycle.
        store.WriteCount.Should().Be(0);
    }

    [Fact]
    public async Task PredictCategoryAsync_AfterTheVaultChanges_DoesNotAnswerFromThePreviousVaultsModel()
    {
        // Arrange - train vault A, then swap the store to vault B, which has no model.
        var store = new FakeCategorizerModelStore { VaultFingerprint = VaultA };
        using var categorizer = NewCategorizer(store);
        var groceries = "Groceries";

        await categorizer.TrainModelAsync(BuildHistory(40, groceries));
        (await categorizer.PredictCategoryAsync("TESCO STORES", 12.50m)).Category
            .Should().Be(groceries, "the model should be live for the vault it was trained in");

        // Act - the user locks Personal and opens Business.
        store.VaultFingerprint = VaultB;
        store.StoredModel = null;
        var result = await categorizer.PredictCategoryAsync("TESCO STORES", 12.50m);

        // Assert - THE CENTRAL TD-16 ASSERTION. "Personal" and "Business" are the reason multi-vault
        // exists; a categoriser that carries one vault's learned payees into the other leaks the
        // first vault's spending through the second vault's suggestions, without either database
        // ever being decrypted.
        result.Category.Should().BeEmpty();
        result.ConfidenceScore.Should().Be(0.0);
    }

    [Fact]
    public async Task PredictCategoryAsync_AfterALockAndTheSameVaultReopening_ReloadsFromTheStore()
    {
        // Arrange
        var store = new FakeCategorizerModelStore { VaultFingerprint = VaultA };
        using var categorizer = NewCategorizer(store);
        var groceries = "Groceries";
        await categorizer.TrainModelAsync(BuildHistory(40, groceries));

        // Act - lock, then reopen the same vault. The engine must be dropped on the lock and
        // rebuilt from the sealed bytes afterwards, not merely kept because the vault came back.
        store.VaultFingerprint = null;
        (await categorizer.PredictCategoryAsync("TESCO STORES", 12.50m)).Category.Should().BeEmpty();

        var readsBeforeReopen = store.ReadCount;
        store.VaultFingerprint = VaultA;
        var result = await categorizer.PredictCategoryAsync("TESCO STORES", 12.50m);

        // Assert
        store.ReadCount.Should().Be(readsBeforeReopen + 1, "the model must be read back, not retained across the lock");
        result.Category.Should().Be(groceries);
    }

    [Fact]
    public async Task PredictCategoryAsync_CalledRepeatedlyForOneVault_ReadsTheStoreOnlyOnce()
    {
        // Arrange - a vault with no stored model, which is the case that would loop forever if
        // "no engine" and "not tried yet" were the same state.
        var store = new FakeCategorizerModelStore { VaultFingerprint = VaultA };
        using var categorizer = NewCategorizer(store);

        // Act
        for (var i = 0; i < 5; i++)
        {
            await categorizer.PredictCategoryAsync("TESCO", 12.50m);
        }

        // Assert - an import categorises every uncategorised row it saves, so a re-read per call is
        // a decrypt per row of a file that has not changed.
        store.ReadCount.Should().Be(1);
    }

    [Fact]
    public async Task PredictCategoryAsync_WhenTheStoredModelIsUnusable_ReturnsNoCategoryRatherThanThrowing()
    {
        // Arrange - bytes that decrypt cleanly but are not an ML.NET model. That is what an upgrade
        // across an incompatible pipeline shape looks like on disk.
        var store = new FakeCategorizerModelStore
        {
            VaultFingerprint = VaultA,
            StoredModel = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0xFF, 0xFF }
        };
        using var categorizer = NewCategorizer(store);

        // Act
        var result = await categorizer.PredictCategoryAsync("TESCO", 12.50m);

        // Assert - a damaged derived artefact must never become a reason the user cannot use their
        // vault. It is discarded and retrained on the next cycle.
        result.Category.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_DoesNotTouchTheStore()
    {
        // Arrange & Act - the container builds this singleton before any vault is open. The old
        // constructor called LoadModelIfExists(), which could only ever have found a file that did
        // not belong to a vault at all -- the defect TD-16 names.
        var store = new FakeCategorizerModelStore { VaultFingerprint = VaultA };
        using var categorizer = NewCategorizer(store);

        // Assert
        store.ReadCount.Should().Be(0);
        store.FingerprintReadCount.Should().Be(0);
    }

    private static MlNetCategorizer NewCategorizer(ICategorizerModelStore store)
        => new(store, NullLogger<MlNetCategorizer>.Instance);

    /// <summary>
    /// Builds a training corpus large enough to clear the row threshold, in which the payee text is
    /// perfectly predictive of the category. Real data is not this clean; these tests are about
    /// which vault answers, not about accuracy, so a separable corpus keeps them deterministic.
    /// </summary>
    /// <summary>
    /// Two categories, alternating, so the model has something to discriminate between.
    /// </summary>
    /// <remarks>
    /// The labels are NAMES rather than <see cref="Guid"/>s (TD-32). They used to be freshly
    /// generated identifiers, which made these tests pass while the product they stood for could
    /// not work: nothing in the application has ever produced a Guid-shaped label, so the real
    /// training set was always empty.
    /// </remarks>
    private static List<TrainingExample> BuildHistory(int rows, string? groceries = null)
    {
        var groceriesLabel = groceries ?? "Groceries";
        const string transport = "Transport";

        return Enumerable.Range(0, rows)
            .Select(i => i % 2 == 0
                ? new TrainingExample($"TESCO STORES {i}", 10m + i, groceriesLabel)
                : new TrainingExample($"UBER TRIP {i}", 20m + i, transport))
            .ToList();
    }

    /// <summary>
    /// A store whose vault can be moved under the categoriser between calls, and which counts what
    /// the categoriser asked for.
    /// </summary>
    private sealed class FakeCategorizerModelStore : ICategorizerModelStore
    {
        private string? _vaultFingerprint;

        public string? VaultFingerprint
        {
            get
            {
                FingerprintReadCount++;
                OnFingerprintRead?.Invoke(FingerprintReadCount);
                return _vaultFingerprint;
            }
            set => _vaultFingerprint = value;
        }

        /// <summary>Invoked on each fingerprint read, so a test can move the vault mid-operation.</summary>
        public Action<int>? OnFingerprintRead { get; set; }

        public byte[]? StoredModel { get; set; }

        public int ReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public int FingerprintReadCount { get; private set; }

        public byte[]? TryRead()
        {
            ReadCount++;

            if (_vaultFingerprint is null)
            {
                throw new VaultLockedException("access the categorisation model");
            }

            return StoredModel;
        }

        public void Write(ReadOnlySpan<byte> model)
        {
            if (model.IsEmpty)
            {
                throw new ArgumentException("Refusing to store an empty categorisation model.", nameof(model));
            }

            if (_vaultFingerprint is null)
            {
                throw new VaultLockedException("access the categorisation model");
            }

            WriteCount++;
            StoredModel = model.ToArray();
        }
    }
}
