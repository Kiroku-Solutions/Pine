using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Ai;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using Sdcb.PaddleInference;
using OpenCvSharp;

namespace Pine.Infrastructure.Ai.Vision;

/// <summary>
/// Uses Sdcb.PaddleOCR to process receipt images.
/// Uses the LatinV5 models for robust extraction in Spanish and other latin languages.
/// </summary>
public sealed class PaddleOcrEngine : IDisposable
{
    private PaddleOcrAll? _engine;
    private bool _initialized;

    public PaddleOcrEngine()
    {
        try
        {
            Initialize();
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "ocr_init_error.log"), $"OCR INIT CRASH: {ex}\n"); } catch {}
            _engine = null;
        }
    }

    private void Initialize()
    {
        try
        {
            // Initialize PaddleOcrAll with the built-in PP-OCRv5 Latin models (perfect for Spanish)
            _engine = new PaddleOcrAll(LocalFullModels.LatinV5, PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = false, 
                Enable180Classification = false
            };
            _initialized = true;
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "ocr_init_error.log"), $"OCR INIT FAIL: {ex}\n"); } catch {}
            _engine = null;
        }
    }

    /// <summary>
    /// Extracts text from a stream and formats it as OcrWord list.
    /// </summary>
    public async Task<List<OcrWord>> ExtractTextAsync(Stream imageStream, CancellationToken cancellationToken)
    {
        if (!_initialized || _engine == null)
        {
            return new List<OcrWord>
            {
                new() { Text = "ERROR_MODELS_NOT_FOUND", Confidence = 1.0f, BoundingBox = new BoundingBox { X = 50, Y = 100, Width = 200, Height = 40 } }
            };
        }

        var blocks = new List<OcrWord>();

        try
        {
            // Copy stream to memory
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms, cancellationToken);
            byte[] imageBytes = ms.ToArray();
            
            // Sdcb uses OpenCvSharp Mat natively
            using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            
            // Run inference
            PaddleOcrResult ocrResult = _engine.Run(mat);

            if (ocrResult?.Regions != null)
            {
                foreach (var region in ocrResult.Regions)
                {
                    // Calculate bounding box from the 4 corners
                    var points = region.Rect.Points();
                    float minX = Math.Min(Math.Min(points[0].X, points[1].X), Math.Min(points[2].X, points[3].X));
                    float maxX = Math.Max(Math.Max(points[0].X, points[1].X), Math.Max(points[2].X, points[3].X));
                    float minY = Math.Min(Math.Min(points[0].Y, points[1].Y), Math.Min(points[2].Y, points[3].Y));
                    float maxY = Math.Max(Math.Max(points[0].Y, points[1].Y), Math.Max(points[2].Y, points[3].Y));
                    
                    blocks.Add(new OcrWord
                    {
                        Text = region.Text,
                        Confidence = region.Score,
                        BoundingBox = new BoundingBox
                        {
                            X = minX,
                            Y = minY,
                            Width = maxX - minX,
                            Height = maxY - minY
                        }
                    });
                    
                    Console.WriteLine($"[OCR] Extracted: '{region.Text}' (Conf: {region.Score:F2})");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OCR EXCEPTION: {ex.Message}");
            return new List<OcrWord>
            {
                new() { Text = "OCR_ERROR_CRASH", Confidence = 1.0f, BoundingBox = new BoundingBox { X = 50, Y = 100, Width = 200, Height = 40 } },
                new() { Text = ex.Message, Confidence = 1.0f, BoundingBox = new BoundingBox { X = 50, Y = 150, Width = 200, Height = 40 } }
            };
        }

        return blocks;
    }

    public void Dispose()
    {
        _engine?.Dispose();
    }
}
