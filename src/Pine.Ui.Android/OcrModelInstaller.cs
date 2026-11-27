using System;
using System.IO;
using Android.Content;
using Android.Content.Res;

namespace Pine.Ui.Android;

/// <summary>
/// Copies the ONNX OCR, YOLO, and optional bundled LayoutLMv3 models from the APK's assets to a real directory
/// on first run (and after an update that changed them): ONNX Runtime loads from a file path, and an asset is
/// not one (docs/65 section 7). Size comparisons via <see cref="AssetManager.OpenFd"/> skip the copy on later starts.
/// </summary>
internal static class OcrModelInstaller
{
    private static readonly string[] OcrAndYoloFiles = ["det.onnx", "rec.onnx", "dict_latin_v5.txt", "yolo-receipt-nano.onnx"];
    private static readonly string[] LayoutLmFiles = ["model.onnx", "tokenizer.json"];

    /// <summary>Installs the OCR and YOLO models and returns the directory that holds them.</summary>
    internal static string Install(Context context)
    {
        var target = Path.Combine(context.FilesDir!.AbsolutePath, "ocr-models");
        Directory.CreateDirectory(target);
        var assets = context.Assets!;

        foreach (var name in OcrAndYoloFiles)
        {
            var assetName = (name.StartsWith("yolo", StringComparison.Ordinal) ? "yolo/" : "ocr/") + name;
            CopyAssetIfNeeded(assets, assetName, Path.Combine(target, name));
        }

        return target;
    }

    /// <summary>True when the APK contains the bundled LayoutLMv3 model and tokenizer assets.</summary>
    internal static bool HasBundledLayoutLm(Context context)
    {
        var assets = context.Assets!;
        return AssetExists(assets, "layoutlm/model.onnx") && AssetExists(assets, "layoutlm/tokenizer.json");
    }

    /// <summary>Returns the target file path for the bundled LayoutLMv3 model under app files.</summary>
    internal static string GetBundledLayoutLmTargetPath(Context context) =>
        Path.Combine(context.FilesDir!.AbsolutePath, "layoutlm", "model.onnx");

    /// <summary>
    /// Installs the bundled LayoutLMv3 model and tokenizer to <c>FilesDir/layoutlm/</c> if missing or changed,
    /// returning the path to <c>model.onnx</c> (or <see langword="null"/> when not bundled in the APK).
    /// </summary>
    internal static string? InstallLayoutLm(Context context)
    {
        if (!HasBundledLayoutLm(context))
        {
            return null;
        }

        var target = Path.Combine(context.FilesDir!.AbsolutePath, "layoutlm");
        Directory.CreateDirectory(target);
        var assets = context.Assets!;

        foreach (var name in LayoutLmFiles)
        {
            CopyAssetIfNeeded(assets, "layoutlm/" + name, Path.Combine(target, name));
        }

        return Path.Combine(target, "model.onnx");
    }

    private static void CopyAssetIfNeeded(AssetManager assets, string assetName, string destination)
    {
        if (File.Exists(destination) && SizeOf(assets, assetName) is { } size && new FileInfo(destination).Length == size)
        {
            return;
        }

        var temporary = destination + ".tmp";
        using (var input = assets.Open(assetName))
        using (var output = File.Create(temporary))
        {
            input.CopyTo(output, bufferSize: 81920);
        }

        File.Move(temporary, destination, overwrite: true);
    }

    private static bool AssetExists(AssetManager assets, string assetName)
    {
        if (SizeOf(assets, assetName) is > 0)
        {
            return true;
        }

        try
        {
            using var stream = assets.Open(assetName);
            return stream is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static long? SizeOf(AssetManager assets, string assetName)
    {
        try
        {
            using var descriptor = assets.OpenFd(assetName);
            return descriptor.Length;
        }
        catch (Exception)
        {
            // A compressed asset has no file descriptor; the caller then copies again.
            return null;
        }
    }
}
