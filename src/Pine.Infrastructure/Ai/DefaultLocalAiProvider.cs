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
/// On-device AI provider that orchestrates the full receipt scanning pipeline:
/// YOLO Detection → Image Preprocessing → PaddleOCR → Heuristic Field Extraction.
/// </summary>
public sealed class DefaultLocalAiProvider : IAIProvider, IDisposable
{
    private readonly ReceiptDetector _detector;
    private readonly PaddleOcrEngine _ocr;
    private readonly HeuristicFieldExtractor _extractor;
    private readonly MlNetCategorizer _categorizer;

    public DefaultLocalAiProvider()
    {
        _detector = new ReceiptDetector();
        _ocr = new PaddleOcrEngine();
        _extractor = new HeuristicFieldExtractor();
        _extractor.LoadModel();
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
        // P0 FIX: Preprocess image before OCR using the ReceiptDetector's
        // adaptive threshold pipeline when possible.
        Stream ocrStream = imageStream;
        try
        {
            imageStream.Position = 0;
            var analysis = await _detector.AnalyzeAndCropReceiptAsync(imageStream, cancellationToken);
            if (analysis.PassedFilters && analysis.OcrImageStream != null)
            {
                // Use the binarized, perspective-corrected image for OCR
                ocrStream = analysis.OcrImageStream;
            }
            else
            {
                // Fallback: use the original image
                imageStream.Position = 0;
                ocrStream = imageStream;
            }
        }
        catch
        {
            // If preprocessing fails, use original
            imageStream.Position = 0;
            ocrStream = imageStream;
        }

        // Execute OCR on the (potentially preprocessed) image
        ocrStream.Position = 0;
        var textBlocks = await _ocr.ExtractTextAsync(ocrStream, cancellationToken);

        // Extract fields using layout-aware heuristics
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
        _extractor?.Dispose();
    }
}
