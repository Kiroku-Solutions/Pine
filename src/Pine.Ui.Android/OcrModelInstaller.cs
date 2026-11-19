using System;
using System.IO;
using Android.Content;
using Android.Content.Res;

namespace Pine.Ui.Android;

/// <summary>
/// Copies the ONNX OCR models from the APK's assets to a real directory on first run (and after an update that
/// changed them): ONNX Runtime loads from a file path, and an asset is not one (docs/65 section 7). 13 MB, a
/// fraction of a second, then a size comparison per file on later starts.
/// </summary>
internal static class OcrModelInstaller
{
    private static readonly string[] Files = ["det.onnx", "rec.onnx", "dict_latin_v5.txt"];

    /// <summary>Installs the models and returns the directory that holds them.</summary>
    internal static string Install(Context context)
    {
        var target = Path.Combine(context.FilesDir!.AbsolutePath, "ocr-models");
        Directory.CreateDirectory(target);
        var assets = context.Assets!;

        foreach (var name in Files)
        {
            var destination = Path.Combine(target, name);
            var assetName = "ocr/" + name;
            if (File.Exists(destination) && SizeOf(assets, assetName) is { } size && new FileInfo(destination).Length == size)
            {
                continue;
            }

            var temporary = destination + ".tmp";
            using (var input = assets.Open(assetName))
            using (var output = File.Create(temporary))
            {
                input.CopyTo(output);
            }

            File.Move(temporary, destination, overwrite: true);
        }

        return target;
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
