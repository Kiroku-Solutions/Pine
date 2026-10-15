using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Pine.Core.Common;

namespace Pine.Infrastructure.Ai.Vision;

public interface IReceiptDetector : IDisposable
{
    Task<bool> DetectReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default);
    Task<float> DetectReceiptConfidenceAsync(Stream imageStream, CancellationToken cancellationToken = default);
    Task<Pine.Core.Ai.ReceiptAnalysisResult> AnalyzeAndCropReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default);
}

/// <remarks>
/// <para>
/// TUNED-CONSTANTS BOUNDARY (TD-118). The numeric thresholds, geometry and kernel parameters in this class are the
/// algorithm, tuned together with the model version (yolo-receipt-nano.onnx: input 640, 8400 anchors, detection cutoffs). They are deliberately NOT in
/// <c>PineOptions</c>: exposing them would let a user break receipt detection and report it as a
/// bug. Configurable limits (paths, sizes, timeouts) belong in options; these do not. Change one
/// only with a model or fixture change that re-evaluates the whole pipeline. See
/// <c>docs/13-AI-Architecture.md</c> section 7.1.
/// </para>
/// </remarks>
public sealed class ReceiptDetector : IReceiptDetector
{
    private static readonly string YoloModelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Ai", "yolo-receipt-nano.onnx");
    private InferenceSession? _session;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isInitialized;

    public ReceiptDetector()
    {
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized) return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized) return;

            if (File.Exists(YoloModelPath))
            {
                var options = new SessionOptions();
                // options.AppendExecutionProvider_CPU();
                _session = new InferenceSession(YoloModelPath, options);
                _isInitialized = true;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DetectReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        float conf = await DetectReceiptConfidenceAsync(imageStream, cancellationToken);
        return conf > 0.5f;
    }

    /// <summary>
    /// Scores how confidently the image contains a receipt.
    /// </summary>
    /// <remarks>
    /// THE ONLY WORK DONE ON THE CALLER'S THREAD HERE IS READING THE STREAM. Everything after it --
    /// JPEG decode, letterbox resize, the pixel loop, and the ONNX session run -- is several seconds
    /// of pure CPU, and it used to run inline. Because this method is awaited from an Avalonia view
    /// model, "inline" meant the UI thread: the window stopped painting and Windows drew its "not
    /// responding" ghost over it. The busy indicator on the scan screen could not help, because a
    /// blocked UI thread cannot animate a spinner either.
    ///
    /// So the CPU half is pushed to the thread pool. That is what <c>OpenCvImageProcessor</c>
    /// already did, and this type simply never got the same treatment.
    /// </remarks>
    public async Task<float> DetectReceiptConfidenceAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (!_isInitialized || _session == null)
        {
            return 0f;
        }

        // 1. Load stream into memory and decode to Mat (OpenCV)
        using var memoryStream = new MemoryStream();
        await imageStream.CopyToAsync(memoryStream, cancellationToken);
        var bytes = memoryStream.ToArray();

        return await Task.Run(() => ScoreReceiptConfidence(_session, bytes), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The CPU-bound half of <see cref="DetectReceiptConfidenceAsync"/>. Never call this on the UI thread.</summary>
    private float ScoreReceiptConfidence(InferenceSession session, byte[] bytes)
    {
        using var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (mat.Empty()) return 0f;

        // 2. State-of-the-art preprocessing for YOLO (Letterbox 640x640, BGR -> RGB, Float Normalize 0-1)
        int targetSize = 640;
        
        float ratio = Math.Min((float)targetSize / mat.Width, (float)targetSize / mat.Height);
        int newW = (int)Math.Round(mat.Width * ratio);
        int newH = (int)Math.Round(mat.Height * ratio);

        using var resized = new Mat();
        Cv2.Resize(mat, resized, new Size(newW, newH));

        int top = (targetSize - newH) / 2;
        int bottom = targetSize - newH - top;
        int left = (targetSize - newW) / 2;
        int right = targetSize - newW - left;

        using var letterbox = new Mat();
        Cv2.CopyMakeBorder(resized, letterbox, top, bottom, left, right, BorderTypes.Constant, new Scalar(114, 114, 114));

        using var rgbMat = new Mat();
        Cv2.CvtColor(letterbox, rgbMat, ColorConversionCodes.BGR2RGB);

        // Create the input tensor expected by YOLOv8 [1, 3, 640, 640]
        var tensor = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });
        
        // Extract pixels (HWC) to Tensor (CHW) by dividing by 255
        unsafe
        {
            byte* p = (byte*)rgbMat.DataPointer;
            long step = rgbMat.Step();
            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    long index = y * step + x * 3;
                    tensor[0, 0, y, x] = p[index] / 255.0f;     // R
                    tensor[0, 1, y, x] = p[index + 1] / 255.0f; // G
                    tensor[0, 2, y, x] = p[index + 2] / 255.0f; // B
                }
            }
        }

        var inputName = session.InputMetadata.Keys.First();
        var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) };

        // 3. ONNX Inference
        using var results = session.Run(inputs);
        
        // 4. Process YOLOv8 output (typically [1, 4+classes, 8400] or [1, 4+classes+32, 8400] for seg)
        var outputTensor = results[0].AsTensor<float>();
        
        float maxConfidence = 0f;

        // In YOLOv8, depending on the training dataset, we might have multiple classes.
        int numClasses = outputTensor.Dimensions[1] - 4; // Total classes (e.g., 1 or 80)
        
        // If it's a segmentation model, it has a second output for mask prototypes,
        // and the last 32 elements of the first output are mask coefficients, NOT confidences.
        if (results.Count > 1 && outputTensor.Dimensions[1] >= 36)
        {
            numClasses -= 32;
        }

        // Safety check to ensure we have at least 1 class
        if (numClasses < 1) numClasses = 1;
        
        for (int i = 0; i < 8400; i++)
        {
            // Bounding box dimensions (w, h)
            float w = outputTensor[0, 2, i];
            float h = outputTensor[0, 3, i];
            
            // Filter out microscopic/noise boxes (e.g. less than ~5% of a 640x640 image)
            // A receipt should be relatively large in the frame.
            if (w < 64 || h < 64)
                continue;

            for (int c = 0; c < numClasses; c++)
            {
                float confidence = outputTensor[0, 4 + c, i];
                if (confidence > maxConfidence)
                {
                    maxConfidence = confidence;
                }
            }
        }

        return maxConfidence;
    }

    /// <summary>
    /// Detects the receipt, corrects its perspective and returns both a display image and an
    /// OCR-optimised one.
    /// </summary>
    /// <remarks>
    /// OFF THE CALLER'S THREAD, for the reason given on <see cref="DetectReceiptConfidenceAsync"/>.
    /// This method is the heavier of the two -- it runs the same inference and then a perspective
    /// warp, a denoise and a sharpen on top -- so it is the one that produced the multi-second
    /// freeze the scan screen was showing.
    /// </remarks>
    public async Task<Pine.Core.Ai.ReceiptAnalysisResult> AnalyzeAndCropReceiptAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (!_isInitialized || _session == null)
        {
            return new Pine.Core.Ai.ReceiptAnalysisResult(0f, false, UserFacingText.ScanNotReady(), null);
        }

        // 1. Load stream into memory and decode to Mat (OpenCV)
        using var memoryStream = new MemoryStream();
        await imageStream.CopyToAsync(memoryStream, cancellationToken);
        var bytes = memoryStream.ToArray();

        return await Task.Run(() => AnalyzeAndCrop(_session, bytes), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The CPU-bound half of <see cref="AnalyzeAndCropReceiptAsync"/>. Never call this on the UI thread.</summary>
    private static Pine.Core.Ai.ReceiptAnalysisResult AnalyzeAndCrop(InferenceSession session, byte[] bytes)
    {
        using var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (mat.Empty()) return new Pine.Core.Ai.ReceiptAnalysisResult(0f, false, UserFacingText.ScanUnreadableFrame(), null);

        // 2. State-of-the-art preprocessing for YOLO (Letterbox 640x640, BGR -> RGB, Float Normalize 0-1)
        int targetSize = 640;
        
        float ratio = Math.Min((float)targetSize / mat.Width, (float)targetSize / mat.Height);
        int newW = (int)Math.Round(mat.Width * ratio);
        int newH = (int)Math.Round(mat.Height * ratio);

        using var resized = new Mat();
        Cv2.Resize(mat, resized, new Size(newW, newH));

        int top = (targetSize - newH) / 2;
        int bottom = targetSize - newH - top;
        int left = (targetSize - newW) / 2;
        int right = targetSize - newW - left;

        using var letterbox = new Mat();
        Cv2.CopyMakeBorder(resized, letterbox, top, bottom, left, right, BorderTypes.Constant, new Scalar(114, 114, 114));

        using var rgbMat = new Mat();
        Cv2.CvtColor(letterbox, rgbMat, ColorConversionCodes.BGR2RGB);

        // Create the input tensor expected by YOLOv8 [1, 3, 640, 640]
        var tensor = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });
        
        // Extract pixels (HWC) to Tensor (CHW) by dividing by 255
        unsafe
        {
            byte* p = (byte*)rgbMat.DataPointer;
            long step = rgbMat.Step();
            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    long index = y * step + x * 3;
                    tensor[0, 0, y, x] = p[index] / 255.0f;     // R
                    tensor[0, 1, y, x] = p[index + 1] / 255.0f; // G
                    tensor[0, 2, y, x] = p[index + 2] / 255.0f; // B
                }
            }
        }

        var inputName = session.InputMetadata.Keys.First();
        var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) };

        // 3. ONNX Inference
        using var results = session.Run(inputs);
        
        // 4. Process YOLOv8 output
        var outputTensor = results[0].AsTensor<float>();
        
        float maxConfidence = 0f;
        int bestAnchor = -1;

        int numClasses = outputTensor.Dimensions[1] - 4; // Total classes (e.g., 1 or 80)
        
        bool isSegmentation = results.Count > 1 && outputTensor.Dimensions[1] >= 36;
        if (isSegmentation)
        {
            numClasses -= 32;
        }

        if (numClasses < 1) numClasses = 1;
        
        for (int i = 0; i < 8400; i++)
        {
            float w = outputTensor[0, 2, i];
            float h = outputTensor[0, 3, i];
            
            if (w < 64 || h < 64)
                continue;

            for (int c = 0; c < numClasses; c++)
            {
                float confidence = outputTensor[0, 4 + c, i];
                if (confidence > maxConfidence)
                {
                    maxConfidence = confidence;
                    bestAnchor = i;
                }
            }
        }

        if (maxConfidence < 0.85f || bestAnchor == -1)
        {
            return new Pine.Core.Ai.ReceiptAnalysisResult(maxConfidence, false, UserFacingText.ScanSearching(), null);
        }

        if (!isSegmentation)
        {
            return new Pine.Core.Ai.ReceiptAnalysisResult(maxConfidence, false, UserFacingText.ScanWrongModel(), null);
        }

        // 5. MASK EXTRACTION
        // The last 32 elements are mask coefficients
        float[] maskCoeffs = new float[32];
        for (int i = 0; i < 32; i++)
        {
            maskCoeffs[i] = outputTensor[0, 4 + numClasses + i, bestAnchor];
        }

        var maskProtos = results[1].AsTensor<float>(); // [1, 32, 160, 160]

        // Create 160x160 mask by dot product
        using var mask160 = new Mat(160, 160, MatType.CV_32FC1);
        unsafe
        {
            float* pMask = (float*)mask160.DataPointer;
            int idx = 0;
            for (int y = 0; y < 160; y++)
            {
                for (int x = 0; x < 160; x++)
                {
                    float sum = 0f;
                    for (int c = 0; c < 32; c++)
                    {
                        sum += maskCoeffs[c] * maskProtos[0, c, y, x];
                    }
                    // Sigmoid
                    pMask[idx++] = 1.0f / (1.0f + (float)Math.Exp(-sum));
                }
            }
        }

        // Resize 160x160 to 640x640
        using var mask640 = new Mat();
        Cv2.Resize(mask160, mask640, new Size(640, 640));

        // Binarize
        using var maskBinary640 = new Mat();
        Cv2.Threshold(mask640, maskBinary640, 0.5, 255, ThresholdTypes.Binary);
        maskBinary640.ConvertTo(maskBinary640, MatType.CV_8UC1);

        // Map back to original image size (undo letterbox)
        // Crop the letterbox padding
        using var croppedBinary = new Mat(maskBinary640, new Rect(left, top, newW, newH));
        using var finalMask = new Mat();
        Cv2.Resize(croppedBinary, finalMask, new Size(mat.Width, mat.Height));

        // 6. FIND CONTOURS
        Cv2.FindContours(finalMask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours == null || contours.Length == 0)
        {
            return new Pine.Core.Ai.ReceiptAnalysisResult(maxConfidence, false, UserFacingText.ScanCannotOutline(), null);
        }

        // Get largest contour
        var largestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
        double perimeter = Cv2.ArcLength(largestContour, true);

        Point[]? bestCorners = null;
        for (int i = 10; i < 150; i++)
        {
            double epsilon = (i / 1000.0) * perimeter;
            var approx = Cv2.ApproxPolyDP(largestContour, epsilon, true);
            if (approx.Length == 4)
            {
                bestCorners = approx;
                break;
            }
        }

        if (bestCorners == null)
        {
            return new Pine.Core.Ai.ReceiptAnalysisResult(maxConfidence, false, UserFacingText.ScanIrregularOutline(), null);
        }

        // Order corners: TL, TR, BR, BL
        var tl = bestCorners.OrderBy(p => p.X + p.Y).First();
        var br = bestCorners.OrderByDescending(p => p.X + p.Y).First();
        var tr = bestCorners.OrderBy(p => p.Y - p.X).First();
        var bl = bestCorners.OrderByDescending(p => p.Y - p.X).First();

        // Width and height
        double widthA = Math.Sqrt(Math.Pow(br.X - bl.X, 2) + Math.Pow(br.Y - bl.Y, 2));
        double widthB = Math.Sqrt(Math.Pow(tr.X - tl.X, 2) + Math.Pow(tr.Y - tl.Y, 2));
        int maxWidth = (int)Math.Max(widthA, widthB);

        double heightA = Math.Sqrt(Math.Pow(tr.X - br.X, 2) + Math.Pow(tr.Y - br.Y, 2));
        double heightB = Math.Sqrt(Math.Pow(tl.X - bl.X, 2) + Math.Pow(tl.Y - bl.Y, 2));
        int maxHeight = (int)Math.Max(heightA, heightB);

        // 7. FILTERS
        int areaPantalla = mat.Width * mat.Height;
        int areaFactura = maxWidth * maxHeight;
        double aspectRatio = Math.Max(maxWidth, maxHeight) / (double)Math.Max(1, Math.Min(maxWidth, maxHeight));

        int margenSeguridad = 15;
        bool tocaBorde = false;
        foreach (var p in bestCorners)
        {
            if (p.X < margenSeguridad || p.X > mat.Width - margenSeguridad || p.Y < margenSeguridad || p.Y > mat.Height - margenSeguridad)
            {
                tocaBorde = true;
                break;
            }
        }

        if (tocaBorde)
            return new Pine.Core.Ai.ReceiptAnalysisResult(maxConfidence, false, UserFacingText.ScanMoveBack(), null);
        if (aspectRatio < 1.15)
            return new Pine.Core.Ai.ReceiptAnalysisResult(maxConfidence, false, UserFacingText.ScanNotRectangular(), null);
        if (areaFactura < areaPantalla * 0.10)
            return new Pine.Core.Ai.ReceiptAnalysisResult(maxConfidence, false, UserFacingText.ScanMoveCloser(), null);

        // 8. WARP PERSPECTIVE
        var srcPts = new[] { new Point2f(tl.X, tl.Y), new Point2f(tr.X, tr.Y), new Point2f(br.X, br.Y), new Point2f(bl.X, bl.Y) };
        var dstPts = new[] { new Point2f(0, 0), new Point2f(maxWidth - 1, 0), new Point2f(maxWidth - 1, maxHeight - 1), new Point2f(0, maxHeight - 1) };
        
        using var M = Cv2.GetPerspectiveTransform(srcPts, dstPts);
        using var warped = new Mat();
        Cv2.WarpPerspective(mat, warped, M, new Size(maxWidth, maxHeight));

        // --- IMAGE ENHANCEMENT PIPELINE ---
        
        // 1. Sharpen for UI (Color)
        using var sharpened = new Mat();
        using var kernel = new Mat(3, 3, MatType.CV_32FC1);
        kernel.SetArray(new float[] { 
             0, -1,  0, 
            -1,  5, -1, 
             0, -1,  0 
        });
        Cv2.Filter2D(warped, sharpened, -1, kernel);

        // 2. Enhance for OCR (B&W Adaptive Threshold)
        using var enhancedForOcr = new Mat();
        Cv2.CvtColor(warped, enhancedForOcr, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(enhancedForOcr, enhancedForOcr, new Size(5, 5), 0);
        Cv2.AdaptiveThreshold(
            enhancedForOcr, 
            enhancedForOcr, 
            maxValue: 255, 
            adaptiveMethod: AdaptiveThresholdTypes.GaussianC, 
            thresholdType: ThresholdTypes.Binary, 
            blockSize: 21, 
            c: 10);

        // Encode both images
        byte[] finalJpg = sharpened.ImEncode(".jpg");
        var resultStream = new MemoryStream(finalJpg);

        byte[] ocrJpg = enhancedForOcr.ImEncode(".jpg");
        var ocrStream = new MemoryStream(ocrJpg);

        return new Pine.Core.Ai.ReceiptAnalysisResult(maxConfidence, true, UserFacingText.ScanCaptured(), resultStream, ocrStream);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _lock.Dispose();
    }
}
