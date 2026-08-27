using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using MediatR;
using Pine.Application.Settings.Queries;

namespace Pine.Ui.Avalonia.Utils;

/// <summary>
/// A selectable accent palette. <see cref="Swatch"/> feeds the settings picker.
/// </summary>
public sealed record ThemePalette(string Key, string Name, Color Swatch)
{
    /// <summary>Swatch as a brush, for direct binding in the palette picker.</summary>
    public IBrush Brush => new SolidColorBrush(Swatch);
}

/// <summary>
/// Runtime theme controller for the shared Avalonia UI. Purely manipulates
/// <see cref="Application.Resources"/> and <see cref="Application.RequestedThemeVariant"/>,
/// so it works identically across every OS head (Windows, macOS, Web, Android, iOS)
/// with no platform-specific code or DI wiring. Persistence is the caller's concern.
/// </summary>
public static class ThemeManager
{
    public const string PaletteSettingKey = "Appearance.Palette";
    public const string VariantSettingKey = "Appearance.Theme";
    public const string DefaultPaletteKey = "Amber";

    /// <summary>All palettes shipped with the app, in display order.</summary>
    public static IReadOnlyList<ThemePalette> Palettes { get; } = new[]
    {
        new ThemePalette("Amber",    "Amber",    Color.Parse("#F57104")),
        new ThemePalette("Amethyst", "Amethyst", Color.Parse("#7C3AED")),
        new ThemePalette("Rose",     "Rose",     Color.Parse("#EC4899")),
        new ThemePalette("Ocean",    "Ocean",    Color.Parse("#2563EB")),
        new ThemePalette("Emerald",  "Emerald",  Color.Parse("#059669")),
    };

    private static ResourceInclude? _appliedPalette;

    /// <summary>Key of the palette currently applied.</summary>
    public static string CurrentPaletteKey { get; private set; } = DefaultPaletteKey;

    /// <summary>Index of the current palette, for two-way binding with a picker.</summary>
    public static int CurrentPaletteIndex =>
        Math.Max(0, IndexOfPalette(CurrentPaletteKey));

    public static int IndexOfPalette(string key)
    {
        for (int i = 0; i < Palettes.Count; i++)
            if (string.Equals(Palettes[i].Key, key, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>Swap the active accent palette by key. Falls back to the default for unknown keys.</summary>
    public static void ApplyPalette(string? key)
    {
        var app = global::Avalonia.Application.Current;
        if (app is null) return;

        if (string.IsNullOrWhiteSpace(key) || IndexOfPalette(key) < 0)
            key = DefaultPaletteKey;

        var uri = new Uri($"avares://Pine.Ui.Avalonia/Themes/{key}Theme.axaml");
        var include = new ResourceInclude(uri) { Source = uri };

        // Remove any previously applied runtime palette; the App.axaml default
        // stays underneath as a safety net.
        if (_appliedPalette is not null)
            app.Resources.MergedDictionaries.Remove(_appliedPalette);

        // Appended last so it wins resource resolution (merged dictionaries are
        // resolved last-to-first in Avalonia).
        app.Resources.MergedDictionaries.Add(include);
        _appliedPalette = include;
        CurrentPaletteKey = key!;
    }

    public static void ApplyPaletteIndex(int index)
    {
        if (index < 0 || index >= Palettes.Count) index = 0;
        ApplyPalette(Palettes[index].Key);
    }

    /// <summary>Set light / dark / system. Index: 0=Light, 1=Dark, 2=System.</summary>
    public static void ApplyVariant(int index)
    {
        var app = global::Avalonia.Application.Current;
        if (app is null) return;

        app.RequestedThemeVariant = index switch
        {
            0 => ThemeVariant.Light,
            1 => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    /// <summary>
    /// Loads the persisted variant + palette for the currently unlocked vault and applies them.
    /// Safe to call after a vault is unlocked; silently no-ops if settings are unavailable.
    /// Never throws.
    /// </summary>
    /// <param name="sender">Mediator used to read the settings.</param>
    /// <returns>A task that completes once the theme has been applied (or skipped).</returns>
    /// <remarks>
    /// Takes an <see cref="ISender"/> rather than an <c>ISettingRepository</c> (TD-01): the
    /// repository is Scoped, and resolving it from the root provider to hand it in here produced a
    /// root-scoped <c>PineDbContext</c> that outlived the vault it was built for. The mediator is
    /// a singleton that opens a fresh scope per request, so each read sees the vault that is
    /// unlocked right now.
    /// </remarks>
    public static async Task ApplyFromSettingsAsync(ISender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);

        try
        {
            var variant = await sender.Send(new GetSettingQuery(VariantSettingKey)).ConfigureAwait(true);
            if (variant != null && int.TryParse(variant, out int variantIndex))
                ApplyVariant(variantIndex);

            var palette = await sender.Send(new GetSettingQuery(PaletteSettingKey)).ConfigureAwait(true);
            if (palette != null)
                ApplyPalette(palette);
        }
        catch
        {
            // Theme is cosmetic — never let a settings failure break the app.
        }
    }
}
