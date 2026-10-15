using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Pine.Core.Configuration;

/// <summary>
/// Finds the configuration keys Pine would silently ignore (TD-117).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. The configuration binder ignores a key that matches no property, so
/// <c>Pine:Ai:ConfidenceThreshold</c> — the natural misspelling of
/// <see cref="PineAiOptions.CategorisationConfidenceThreshold"/> — changed nothing and reported nothing,
/// and the operator's experience was that configuration does not work. Pine already refused a value
/// outside its bounds at startup; a key now refuses the same way, in the same exception.
/// </para>
/// <para>
/// WHY IT LIVES HERE AND TAKES KEYS AS STRINGS. Its sentences are the same kind as
/// <see cref="PineOptions.Validate"/>'s — for a person editing a file, read from the crash log, never
/// shown on a screen — so they sit beside those rather than in the presentation layer, whose C# holds no
/// English sentences (TD-150). Taking the keys and values as strings keeps this assembly BCL-only; the caller
/// flattens its <c>IConfiguration</c>.
/// </para>
/// <para>
/// THE KNOWN KEYS ARE READ FROM <see cref="PineOptions"/> ITSELF, not listed here. A settable property
/// of a primitive, <see cref="string"/>, <see cref="decimal"/> or <see cref="TimeSpan"/> type is a
/// setting; a settable property of any other type is a section. A list kept by hand beside the options
/// tree would be the next thing to drift from it. A read-only property
/// (<see cref="PineStorageOptions.VaultsDirectory"/>) is not a key: the binder ignores it, so a file
/// setting it believes it did something it did not.
/// </para>
/// <para>
/// UNDER <c>Pine</c>, EVERY KEY IS CHECKED. OUTSIDE IT, ONLY KEYS THAT ARE PLAINLY PINE'S. The root holds
/// whatever the <c>PINE_</c> environment prefix picked up, and that prefix is shared: the packaging
/// scripts read <c>PINE_SIGNTOOL</c>, <c>PINE_SIGNING_CERT</c> and <c>PINE_TIMESTAMP_URL</c>, which arrive
/// as the top-level keys <c>SIGNTOOL</c> and so on. Refusing every unknown top-level key would stop Pine
/// starting on any machine that has built a signed release. What IS refused at the top level is a key
/// whose first segment names a Pine section: <c>PINE_Ai__CategorisationConfidenceThreshold</c> is that
/// variable with its <c>Pine__</c> forgotten, the likeliest mistake an environment variable invites.
/// </para>
/// </remarks>
public static class PineConfigurationKeys
{
    private const char Separator = ':';

    /// <summary>Keys this close to a real one are offered as "did you mean"; see <see cref="Describe"/>.</summary>
    private const int MeaningfulOverlap = 4;

    /// <summary>
    /// Every key Pine would ignore, as one sentence each.
    /// </summary>
    /// <param name="entries">
    /// The configuration's keys and values, <c>:</c>-separated, as <c>IConfiguration.AsEnumerable()</c>
    /// yields them. An entry with no value is a section heading, not something set, and is skipped.
    /// </param>
    /// <returns>One sentence per unknown key, in path order; empty when every key is one Pine reads.</returns>
    /// <remarks>
    /// A VALUE SET ON A SECTION IS REFUSED TOO (found by this item's own review pass). <c>"Ai": 5</c> in the
    /// file, or <c>PINE_Pine__Ai=5</c>, names a key that exists — the section — so a check on names alone
    /// accepted it, and the binder ignored the value. That is TD-117's defect in a different shape.
    /// </remarks>
    public static IReadOnlyList<string> FindUnknown(IEnumerable<KeyValuePair<string, string?>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var keyPaths = entries.Where(entry => entry.Value is not null).Select(entry => entry.Key);

        var problems = new List<string>();
        var reported = new List<string>();
        var sections = SettableProperties(typeof(PineOptions))
            .Where(p => !IsSetting(p.PropertyType))
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var path in keyPaths.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            // Report the shallowest unknown segment once, not every key beneath it.
            if (reported.Any(prefix => path.StartsWith(prefix + Separator, StringComparison.OrdinalIgnoreCase)
                                       || path.Equals(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var segments = path.Split(Separator);

            if (!segments[0].Equals(PineOptions.SectionName, StringComparison.OrdinalIgnoreCase))
            {
                if (sections.TryGetValue(segments[0], out var section))
                {
                    reported.Add(segments[0]);
                    problems.Add(
                        $"{segments[0]} is not a setting Pine reads, so it would be ignored. " +
                        $"Did you mean {PineOptions.SectionName}:{section.Name}? " +
                        $"As an environment variable, that is PINE_{PineOptions.SectionName}__{section.Name}__<setting>.");
                }

                continue;
            }

            var type = typeof(PineOptions);
            var walked = segments[0];
            var endedOnSection = true;
            for (var i = 1; i < segments.Length; i++)
            {
                var known = SettableProperties(type).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
                var here = walked + Separator + segments[i];

                if (!known.TryGetValue(segments[i], out var property))
                {
                    reported.Add(here);
                    problems.Add(Describe(here, segments[i], walked, known.Keys));
                    endedOnSection = false;
                    break;
                }

                if (IsSetting(property.PropertyType))
                {
                    if (i < segments.Length - 1)
                    {
                        reported.Add(here);
                        problems.Add($"{here} is a single value, so the keys beneath it would be ignored. Set {here} itself.");
                    }

                    endedOnSection = false;
                    break;
                }

                type = property.PropertyType;
                walked = here;
            }

            if (endedOnSection)
            {
                var example = SettableProperties(type).Select(p => p.Name).Order(StringComparer.Ordinal).First();
                reported.Add(walked);
                problems.Add($"{walked} is a section, so a single value given for it would be ignored. Set the settings inside it, such as {walked}:{example}.");
            }
        }

        return problems;
    }

    private static string Describe(string path, string name, string parentPath, IEnumerable<string> known)
    {
        var names = known.Order(StringComparer.Ordinal).ToList();

        // "Did you mean" only when exactly one candidate is close: the unknown name is part of a real one
        // (the TD-117 case, ConfidenceThreshold for CategorisationConfidenceThreshold), a real one is part
        // of it, or it is a few typed characters away. Containment needs four characters, because
        // "Secruity" contains "ui" and Ui is not what was meant. Otherwise list the section, which is
        // always right.
        var meant = names
            .Where(candidate => (name.Length >= MeaningfulOverlap && candidate.Contains(name, StringComparison.OrdinalIgnoreCase))
                                || (candidate.Length >= MeaningfulOverlap && name.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                                || EditDistance(candidate, name) <= Math.Max(2, candidate.Length / 5))
            .ToList();

        var sentence = $"{path} is not a setting Pine reads, so it would be ignored. ";

        return meant.Count == 1
            ? sentence + $"Did you mean {parentPath}:{meant[0]}?"
            : sentence + $"The settings in {parentPath} are: {string.Join(", ", names)}.";
    }

    private static IEnumerable<PropertyInfo> SettableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0);

    private static bool IsSetting(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(TimeSpan);

    private static int EditDistance(string a, string b)
    {
        a = a.ToUpperInvariant();
        b = b.ToUpperInvariant();
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();

        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            previous = current;
        }

        return previous[b.Length];
    }
}
