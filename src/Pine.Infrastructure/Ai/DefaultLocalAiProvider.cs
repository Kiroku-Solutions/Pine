using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Vision;
using Pine.Infrastructure.Ai.Extraction;
using Pine.Infrastructure.Ai.Categorisation;

namespace Pine.Infrastructure.Ai;

/// <summary>
/// Skeleton implementation for the on-device AI provider.
/// Orchestrates the OCR, YOLO, ML.NET and Heuristics components.
/// </summary>
public sealed class DefaultLocalAiProvider : IAIProvider, IDisposable
{
    private readonly ReceiptDetector _detector;
    private readonly PaddleOcrEngine _ocr;
    private readonly HeuristicFieldExtractor _extractor;
    private readonly MlNetCategorizer _categorizer;

    public DefaultLocalAiProvider()
    {
        // In a real architecture, this would be injected via DI (Dependency Injection).
        // Instantiating here temporarily to not break compilation until dependencies are registered.
        _detector = new ReceiptDetector();
        _ocr = new PaddleOcrEngine();
        _extractor = new HeuristicFieldExtractor();
        _categorizer = new MlNetCategorizer();
    }

    public async Task<bool> DetectReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        return await _detector.DetectReceiptAsync(imageStream, cancellationToken);
    }

    public async Task<float> DetectReceiptConfidenceAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        return await _detector.DetectReceiptConfidenceAsync(imageStream, cancellationToken);
    }

    public async Task<ReceiptAnalysisResult> AnalyzeAndCropReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        return await _detector.AnalyzeAndCropReceiptAsync(imageStream, cancellationToken);
    }

    public async Task<ReceiptExtractionResult> ExtractReceiptDataAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        // 1. (Optional) Use detector to crop image before OCR
        // var isReceipt = await _detector.DetectReceiptAsync(imageStream, cancellationToken);
        
        // 2. Execute OCR on the document to get the blocks
        var textBlocks = await _ocr.ExtractTextAsync(imageStream, cancellationToken);
        
        // 3. Extract fields using Heuristics (Regex/Geometry)
        return _extractor.ExtractFields(textBlocks);
    }

    public async Task<CategorisationResult> CategorizeTransactionAsync(string transactionText, decimal amount, CancellationToken cancellationToken = default)
    {
        return await _categorizer.PredictCategoryAsync(transactionText, amount, cancellationToken);
    }

    public void Dispose()
    {
        _detector?.Dispose();
        _ocr?.Dispose();
        _categorizer?.Dispose();
    }
}
