using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Pine.Core.Security;

/// <summary>
/// The small embedded list of ordinary English and Spanish words an attacker's passphrase mode
/// tries first (TD-73), ranked most-common first.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT EXISTS. <see cref="PasswordStrengthPolicy"/> used to score <c>correct horse battery staple</c>
/// as if its letters were random, which over-estimates it by a wide margin: an attacker enumerates
/// WORDS, not letters. With this list a word costs about <c>log2(rank + 1)</c> bits.
/// </para>
/// <para>
/// WHY AN EMBEDDED RESOURCE AND NOT AN ARRAY LITERAL. A few hundred words in source would bury the
/// estimator; a text file is reviewable and extendable by anyone. It ships inside the assembly: no
/// file lookup, no download, no network (ADR-0012, and Pine has no network code at all). The list is
/// deliberately small. Words that are NOT in it are scored as random characters, which is the
/// generous direction the floor's margin already covers - so a small list can only fail toward
/// "slightly too strong for an uncommon word", never toward accepting a common-word passphrase it
/// knows about.
/// </para>
/// <para>
/// Entries are lower-case, unaccented and at least <see cref="MinimumWordLength"/> letters. The ORDER
/// is the data: appending is safe, inserting shifts every later rank.
/// </para>
/// </remarks>
internal static class NaturalWords
{
    /// <summary>Shortest word that is matched. Shorter hits are too common in random text to be a signal.</summary>
    internal const int MinimumWordLength = 4;

    /// <summary>The manifest name the text file is embedded under (see <c>Pine.Core.csproj</c>).</summary>
    internal const string ResourceName = "Pine.Core.Security.common-words.txt";

    private static readonly Dictionary<string, int> RankByWord;

    /// <summary>The longest entry, bounding the window the matcher has to try.</summary>
    internal static int MaxLength { get; }

    static NaturalWords()
    {
        RankByWord = Load(out var longest);
        MaxLength = longest;
    }

    /// <summary>The number of distinct words loaded.</summary>
    internal static int Count => RankByWord.Count;

    /// <summary>The zero-based rank of an already case-, accent- and leet-folded candidate, or <c>-1</c>.</summary>
    internal static int RankOf(string candidate)
        => RankByWord.TryGetValue(candidate, out var rank) ? rank : -1;

    private static Dictionary<string, int> Load(out int longestWord)
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        longestWord = 0;

        // A missing or unreadable resource must not throw from a static constructor (that would be an
        // unopenable app); it yields an empty list, and a unit test pins that the list is not empty.
        using var stream = typeof(NaturalWords).Assembly.GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            return ranks;
        }

        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            foreach (var raw in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var word = raw.ToLowerInvariant();

                if (word.Length >= MinimumWordLength && ranks.TryAdd(word, ranks.Count) && word.Length > longestWord)
                {
                    longestWord = word.Length;
                }
            }
        }

        return ranks;
    }
}
