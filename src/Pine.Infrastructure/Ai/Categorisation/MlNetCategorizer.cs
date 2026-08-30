using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using Pine.Core.Ai;
using Pine.Core.Security;

namespace Pine.Infrastructure.Ai.Categorisation;

public interface IMlNetCategorizer
{
    Task<CategorisationResult> PredictCategoryAsync(string transactionText, decimal amount, CancellationToken cancellationToken = default);
}

// ML.NET Data Structures
public class TransactionInput
{
    [LoadColumn(0)] public string Text { get; set; } = string.Empty;
    [LoadColumn(1)] public float Amount { get; set; }
    [LoadColumn(2)] public string CategoryId { get; set; } = string.Empty;
}

public class TransactionPrediction
{
    [ColumnName("PredictedLabel")] public string PredictedCategoryId { get; set; } = string.Empty;
    [ColumnName("Score")] public float[] Scores { get; set; } = Array.Empty<float>();
}

/// <summary>
/// The ML.NET transaction categoriser, scoped to whichever vault is currently open (TD-16).
/// </summary>
/// <remarks>
/// <para>
/// WHAT TD-16 CHANGED HERE. This class used to hold a relative path to a plaintext model file and
/// load it in its constructor. It now holds no path at all: <see cref="ICategorizerModelStore"/>
/// owns the location and the encryption, and this class owns only the model in memory and the
/// question of whether that model still belongs to the vault that is open.
/// </para>
/// <para>
/// LOADING IS LAZY BECAUSE THE CONSTRUCTOR RUNS TOO EARLY. This is a singleton, so it is
/// constructed when the container is built -- at which point no vault is open and there is nothing
/// to load. The old constructor's <c>LoadModelIfExists()</c> therefore never found a model belonging
/// to the user, only whatever file happened to sit next to the executable; that it worked at all was
/// a consequence of the model being shared across vaults, which is the defect.
/// </para>
/// <para>
/// STALENESS IS PREVENTED BY COMPARISON, NOT BY NOTIFICATION. Every entry point calls
/// <see cref="SynchroniseWithVault"/> while holding the lock, which compares the store's current
/// fingerprint against the one the loaded engine came from and drops the engine when they differ.
/// The check runs at the moment an answer is about to be produced, so there is no window in which a
/// previous vault's model can answer a question -- and no dependence on a lock event arriving
/// first. Subscribing to <c>IVaultSession.Locked</c> would have given a window plus an
/// undisposed subscription on a singleton, which is TD-34's problem class.
/// </para>
/// <para>
/// THE SECOND DEFECT THIS CLOSES, WHICH TD-16 DOES NOT MENTION. There used to be two instances of
/// this type in the Windows head: one registered as <c>ICategorizationTrainer</c> and a second that
/// <c>DefaultLocalAiProvider</c> constructed for itself. Training rebuilt the engine on the first;
/// every prediction went through the second. Training therefore had no effect on prediction until
/// the process restarted. Both interfaces now resolve to one registered instance.
/// </para>
/// </remarks>
public sealed partial class MlNetCategorizer : IMlNetCategorizer, ICategorizationTrainer, IDisposable
{
    /// <summary>
    /// Rows below which training is refused. Fewer than this and the model memorises rather than
    /// generalises, and a confidently wrong category is worse than no category.
    /// </summary>
    public const int MinimumTrainingRows = 10;

    private readonly MLContext _mlContext;
    private readonly ICategorizerModelStore _modelStore;
    private readonly ILogger<MlNetCategorizer> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private PredictionEngine<TransactionInput, TransactionPrediction>? _predictionEngine;

    /// <summary>
    /// The vault fingerprint <see cref="_predictionEngine"/> was built for, or <c>null</c> when no
    /// engine is loaded. Compared, never interpreted.
    /// </summary>
    private string? _loadedFingerprint;

    /// <summary>
    /// Whether a load has already been attempted for <see cref="_loadedFingerprint"/>. Without this
    /// a vault with no model yet would re-read the store on every single prediction, because "no
    /// engine" and "not tried yet" would be the same state.
    /// </summary>
    private bool _loadAttempted;

    /// <summary>Creates the categoriser over a vault-scoped model store.</summary>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public MlNetCategorizer(ICategorizerModelStore modelStore, ILogger<MlNetCategorizer> logger)
    {
        ArgumentNullException.ThrowIfNull(modelStore);
        ArgumentNullException.ThrowIfNull(logger);

        _modelStore = modelStore;
        _logger = logger;

        // Instantiate ML.NET context. We use a seed for deterministic reproducibility.
        _mlContext = new MLContext(seed: 42);
    }

    public async Task<CategorisationResult> PredictCategoryAsync(string transactionText, decimal amount, CancellationToken cancellationToken = default)
    {
        var input = new TransactionInput
        {
            Text = transactionText,
            Amount = (float)amount
        };

        TransactionPrediction prediction;

        // PredictionEngine is NOT thread-safe, so we use a semaphore for asynchronous inference.
        // The vault check happens INSIDE the same lock: doing it outside would let a lock land
        // between the check and the Predict call, which is the exact race the check exists to stop.
        await _lock.WaitAsync(cancellationToken);
        try
        {
            SynchroniseWithVault();

            if (_predictionEngine is null)
            {
                // No trained model for this vault yet -- or the vault is locked. Either way, do not
                // predict anything with confidence.
                return new CategorisationResult(Guid.Empty, 0.0);
            }

            prediction = _predictionEngine.Predict(input);
        }
        finally
        {
            _lock.Release();
        }

        // The maximum Score corresponds to the predicted category
        double confidence = prediction.Scores.Length > 0 ? prediction.Scores.Max() : 0.0;

        Guid predictedGuid = Guid.TryParse(prediction.PredictedCategoryId, out var guid) ? guid : Guid.Empty;

        return new CategorisationResult(predictedGuid, confidence);
    }

    public async Task TrainModelAsync(IEnumerable<(string Text, decimal Amount, Guid CategoryId)> history, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(history);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // The vault this training run belongs to, captured BEFORE the expensive work. Training
            // takes real time, and the idle auto-lock (TD-11) runs on a timer thread that does not
            // wait for it.
            var fingerprintAtStart = _modelStore.VaultFingerprint;
            if (fingerprintAtStart is null)
            {
                throw new VaultLockedException("The vault is locked, so the categorisation model cannot be trained.");
            }

            SynchroniseWithVault();

            // 1. Map history to ML.NET input structures
            var trainingData = history.Select(h => new TransactionInput
            {
                Text = h.Text,
                Amount = (float)h.Amount,
                CategoryId = h.CategoryId.ToString()
            }).ToList();

            if (trainingData.Count < MinimumTrainingRows)
            {
                // Not enough data to train a model without completely overfitting
                return;
            }

            // 2. Load data into ML.NET memory
            var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

            // 3. Create the Machine Learning Pipeline
            // This is the state of the art for short text classification in C#:
            // - Map the label to a key (required for multiclass algorithms).
            // - Extract features (FeaturizeText) from the transaction text (creates n-grams, TF-IDF).
            // - Concatenate features: processed text + Amount.
            // - Algorithm: SdcaMaximumEntropy (ideal for multiclass text classification).
            var pipeline = _mlContext.Transforms.Conversion.MapValueToKey(inputColumnName: nameof(TransactionInput.CategoryId), outputColumnName: "Label")
                .Append(_mlContext.Transforms.Text.FeaturizeText(inputColumnName: nameof(TransactionInput.Text), outputColumnName: "TextFeatures"))
                .Append(_mlContext.Transforms.Concatenate("Features", "TextFeatures", nameof(TransactionInput.Amount)))
                .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            // 4. Train the model
            var model = pipeline.Fit(dataView);

            cancellationToken.ThrowIfCancellationRequested();

            // 5. REFUSE TO WRITE IF THE VAULT MOVED UNDER US. Training may have run for seconds; if
            // the vault locked or was swapped in that time, this model was trained on data the
            // current vault does not own and sealing it would file one user's model in another
            // user's vault. Dropping it costs one training cycle.
            if (!string.Equals(_modelStore.VaultFingerprint, fingerprintAtStart, StringComparison.Ordinal))
            {
                LogVaultChangedDuringTraining(_logger);
                return;
            }

            // 6. Serialise, then hand the bytes to the store, which encrypts them. This class never
            // writes a file and never sees the vault key.
            using var buffer = new MemoryStream();
            _mlContext.Model.Save(model, dataView.Schema, buffer);
            _modelStore.Write(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));

            // 7. Reload PredictionEngine so subsequent transactions use the updated model. The
            // outgoing engine is disposed rather than dropped on the floor -- it holds native ML.NET
            // resources, and the previous version of this method leaked one on every training cycle.
            ReplaceEngine(_mlContext.Model.CreatePredictionEngine<TransactionInput, TransactionPrediction>(model), fingerprintAtStart);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Brings the in-memory engine into agreement with whichever vault is open right now. MUST be
    /// called while holding <see cref="_lock"/>.
    /// </summary>
    private void SynchroniseWithVault()
    {
        var fingerprint = _modelStore.VaultFingerprint;

        if (fingerprint is null)
        {
            // Locked. Drop the engine: it was built from a vault whose key has been zeroed, and
            // keeping it would leave a model trained on that vault's data answering questions.
            ReplaceEngine(null, null);
            _loadAttempted = false;
            return;
        }

        if (string.Equals(fingerprint, _loadedFingerprint, StringComparison.Ordinal) && _loadAttempted)
        {
            return;
        }

        ReplaceEngine(null, null);
        _loadedFingerprint = fingerprint;
        _loadAttempted = true;

        byte[]? modelBytes;
        try
        {
            modelBytes = _modelStore.TryRead();
        }
        catch (VaultLockedException)
        {
            // Locked between the fingerprint read and the store read. Fail closed and try again on
            // the next call rather than treating it as an error.
            _loadedFingerprint = null;
            _loadAttempted = false;
            return;
        }

        if (modelBytes is null || modelBytes.Length == 0)
        {
            return;
        }

        try
        {
            using var buffer = new MemoryStream(modelBytes, writable: false);
            var model = _mlContext.Model.Load(buffer, out _);
            _predictionEngine = _mlContext.Model.CreatePredictionEngine<TransactionInput, TransactionPrediction>(model);
        }
        catch (Exception ex)
        {
            // A blob that decrypted correctly but is not a model ML.NET can load. That means it was
            // written by a different ML.NET version or a different pipeline shape -- a real
            // possibility across upgrades. Same policy as an undecryptable one: discard, retrain,
            // do not let it stop the user. Caught broadly on purpose: ML.NET does not document a
            // closed set of load failures, and the correct response is identical for all of them.
            LogModelLoadFailed(_logger, ex);
            ReplaceEngine(null, null);
            _loadedFingerprint = fingerprint;
        }
    }

    /// <summary>Swaps the engine, disposing the one being replaced. MUST be called under the lock.</summary>
    private void ReplaceEngine(PredictionEngine<TransactionInput, TransactionPrediction>? engine, string? fingerprint)
    {
        if (!ReferenceEquals(_predictionEngine, engine))
        {
            _predictionEngine?.Dispose();
        }

        _predictionEngine = engine;

        if (fingerprint is not null)
        {
            _loadedFingerprint = fingerprint;
            _loadAttempted = true;
        }
        else if (engine is null)
        {
            _loadedFingerprint = null;
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
        _predictionEngine?.Dispose();
    }

    // The source-generated LoggerMessage delegates. CA1848 is an ERROR in this repository, so every
    // record goes through one of these; the generator stamps the dotted, snake_case EventName that
    // 30-Observability §4.1 requires. Neither message carries a payee, an amount, a category or a
    // vault path -- a categorisation log line must not become a transaction log line.

    [LoggerMessage(
        EventId = 1,
        EventName = "ai.categorizer.vault_changed_during_training",
        Level = LogLevel.Information,
        Message = "The vault changed while the categorisation model was training; the result was discarded.")]
    private static partial void LogVaultChangedDuringTraining(ILogger logger);

    [LoggerMessage(
        EventId = 2,
        EventName = "ai.categorizer.model_load_failed",
        Level = LogLevel.Warning,
        Message = "The stored categorisation model could not be loaded; it will be retrained.")]
    private static partial void LogModelLoadFailed(ILogger logger, Exception exception);
}
