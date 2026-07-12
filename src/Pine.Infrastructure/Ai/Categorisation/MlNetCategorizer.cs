using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML;
using Microsoft.ML.Data;
using Pine.Core.Ai;

namespace Pine.Infrastructure.Ai.Categorisation;

public interface IMlNetCategorizer
{
    Task<CategorisationResult> PredictCategoryAsync(string transactionText, decimal amount, CancellationToken cancellationToken = default);
    Task TrainModelAsync(IEnumerable<(string Text, decimal Amount, Guid CategoryId)> history, CancellationToken cancellationToken = default);
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

public sealed class MlNetCategorizer : IMlNetCategorizer, IDisposable
{
    private const string ModelPath = "Assets/Ai/categorizer_model.zip";
    private readonly MLContext _mlContext;
    private PredictionEngine<TransactionInput, TransactionPrediction>? _predictionEngine;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MlNetCategorizer()
    {
        // Instantiate ML.NET context. We use a seed for deterministic reproducibility.
        _mlContext = new MLContext(seed: 42);
        LoadModelIfExists();
    }

    private void LoadModelIfExists()
    {
        if (File.Exists(ModelPath))
        {
            var model = _mlContext.Model.Load(ModelPath, out var schema);
            _predictionEngine = _mlContext.Model.CreatePredictionEngine<TransactionInput, TransactionPrediction>(model);
        }
    }

    public async Task<CategorisationResult> PredictCategoryAsync(string transactionText, decimal amount, CancellationToken cancellationToken = default)
    {
        if (_predictionEngine == null)
        {
            // If the user does not have a trained model yet, do not predict anything with confidence
            return new CategorisationResult(Guid.Empty, 0.0);
        }

        var input = new TransactionInput
        {
            Text = transactionText,
            Amount = (float)amount
        };

        TransactionPrediction prediction;

        // PredictionEngine is NOT thread-safe, so we use a semaphore for asynchronous inference
        await _lock.WaitAsync(cancellationToken);
        try
        {
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
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // 1. Map history to ML.NET input structures
            var trainingData = history.Select(h => new TransactionInput
            {
                Text = h.Text,
                Amount = (float)h.Amount,
                CategoryId = h.CategoryId.ToString()
            }).ToList();

            if (trainingData.Count < 10)
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

            // 5. Physically save the model to disk (transparent encryption in final phase)
            var directory = Path.GetDirectoryName(ModelPath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            _mlContext.Model.Save(model, dataView.Schema, ModelPath);

            // 6. Reload PredictionEngine so subsequent transactions use the updated model
            _predictionEngine = _mlContext.Model.CreatePredictionEngine<TransactionInput, TransactionPrediction>(model);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
        _predictionEngine?.Dispose();
    }
}
