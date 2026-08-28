using System;
using System.Collections.Generic;

namespace Pine.Core.Security;

/// <summary>
/// The small embedded list of the passwords an offline attacker tries first, in descending order of
/// how often they appear in public breach corpora.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT IS SMALL, AND WHY THAT IS NOT A COMPROMISE: a full breach corpus is tens of millions of
/// entries and hundreds of megabytes. Shipping one would mean a download (forbidden — Pine has no
/// network code at all, and <c>6.7</c> of the audit says so as a load-bearing fact), a package
/// dependency (forbidden — <c>Pine.Core</c> has no references, see <c>ImportKeyFactory</c> for the
/// precedent), or a multi-megabyte embedded resource in the domain assembly. What the short list
/// buys is the part that matters: the head of the distribution. A handful of hundreds of passwords
/// covers a disproportionate share of real-world choices, and everything past the head is caught
/// by the pattern penalties instead of by lookup.
/// </para>
/// <para>
/// The ORDER is the data. <see cref="PasswordStrengthPolicy"/> scores a hit as
/// <c>log2(rank + 1)</c>, so <c>123456</c> at rank 0 costs 0 bits and an entry near the end costs
/// about 8. That is the honest model of a ranked dictionary attack: the attacker walks the list in
/// this order, so position in the list IS the guess count. Re-ordering this array therefore changes
/// scores, and appending is always safe while inserting is not.
/// </para>
/// <para>
/// Entries are stored lower-case and unsubstituted. The matcher folds case and leetspeak before
/// looking a candidate up, which is why <c>p@ssw0rd</c> does not need its own entry.
/// </para>
/// </remarks>
internal static class CommonPasswords
{
    /// <summary>
    /// The longest entry, so the matcher knows the widest window it has to try at each position and
    /// does not attempt substrings that cannot possibly match.
    /// </summary>
    internal static int MaxLength { get; }

    private static readonly Dictionary<string, int> RankByPassword;

    /// <summary>
    /// Returns the zero-based rank of <paramref name="candidate"/> in the list, or <c>-1</c>.
    /// </summary>
    /// <param name="candidate">An already case-folded, already leet-folded candidate.</param>
    internal static int RankOf(string candidate)
        => RankByPassword.TryGetValue(candidate, out var rank) ? rank : -1;

    /// <summary>The number of entries, used to bound the cost of the worst-ranked hit.</summary>
    internal static int Count => RankByPassword.Count;

    static CommonPasswords()
    {
        RankByPassword = new Dictionary<string, int>(Ordered.Length, StringComparer.Ordinal);

        var longest = 0;

        for (var rank = 0; rank < Ordered.Length; rank++)
        {
            var entry = Ordered[rank];

            // A duplicate would silently shift every later rank's meaning, so the FIRST (most
            // common) position wins and the duplicate is ignored rather than throwing in a static
            // constructor — a TypeInitializationException here would surface as an unopenable app.
            if (RankByPassword.TryAdd(entry, rank) && entry.Length > longest)
            {
                longest = entry.Length;
            }
        }

        MaxLength = longest;
    }

    private static readonly string[] Ordered =
    [
        "123456", "password", "123456789", "12345678", "12345", "qwerty", "1234567", "111111",
        "123123", "abc123", "1234567890", "1234", "iloveyou", "000000", "qwerty123", "1q2w3e",
        "aa12345678", "654321", "123321", "qwertyuiop", "monkey", "dragon", "letmein", "sunshine",
        "princess", "football", "baseball", "welcome", "shadow", "master", "666666", "qwe123",
        "michael", "superman", "1qaz2wsx", "7777777", "121212", "000000000", "passw0rd", "trustno1",
        "jordan", "harley", "robert", "matthew", "daniel", "andrew", "joshua", "hunter",
        "buster", "soccer", "tigger", "charlie", "jennifer", "thomas", "jessica", "pepper",
        "ginger", "hockey", "killer", "george", "sexy", "andrea", "computer", "amanda",
        "summer", "orange", "biteme", "freedom", "banana", "chelsea", "diamond", "yellow",
        "nicole", "cheese", "internet", "starwars", "cookie", "maggie", "mickey", "secret",
        "flower", "hello", "whatever", "purple", "ashley", "nicolas", "jasper", "peanut",
        "silver", "richard", "samantha", "heather", "hammer", "summer1", "corvette", "chicken",
        "camaro", "please", "coffee", "chocolate", "cricket", "phoenix", "guitar", "jackson",
        "london", "liverpool", "arsenal", "barcelona", "chelsea1", "manchester", "rangers", "celtic",
        "admin", "administrator", "root", "toor", "guest", "test", "test123", "changeme",
        "default", "pass", "passwd", "login", "user", "welcome1", "password1", "password123",
        "letmein1", "abcd1234", "a1b2c3", "zaq12wsx", "qazwsx", "asdfgh", "asdfghjkl", "zxcvbnm",
        "qweasdzxc", "1qazxsw2", "poiuytrewq", "mnbvcxz", "88888888", "5201314", "woaini1314", "a123456",
        "123456a", "159753", "987654321", "112233", "102030", "789456", "147258369", "555555",
        "222222", "999999", "1111111111", "abcdefg", "abcdef", "qwerty1", "1234qwer", "iloveyou1",
        "sunshine1", "michael1", "jordan23", "soccer1", "baseball1", "football1", "superman1", "batman",
        "spiderman", "pokemon", "minecraft", "starwars1", "gandalf", "matrix", "ninja", "samsung",
        "google", "facebook", "linkedin", "myspace1", "yahoo", "hotmail", "gmail", "bitcoin",
        "money", "moneymoney", "cashmoney", "bank", "vault", "finance", "budget", "pine",
    ];
}
