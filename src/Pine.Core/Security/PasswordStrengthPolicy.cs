using System;
using System.Collections.Generic;
using System.Linq;

namespace Pine.Core.Security;

/// <summary>
/// Answers one question: is this master password strong enough to be the SOLE protection of an
/// encrypted vault, and if not, why not?
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS (TD-11). The audit's finding is exact: <c>SetupViewModel</c> rejected only a
/// whitespace-empty password, so a one-character master password was accepted, and "Argon2id at
/// 64 MB is meaningless against a one-character password". Pine has no server, no rate limiter and
/// no account lockout - the attacker holds the ciphertext and guesses at their own pace. The KDF
/// buys a fixed multiplier on the cost of each guess; the password supplies the NUMBER of guesses.
/// Only the second of those two is under the user's control at setup, and until now nothing
/// measured it.
/// </para>
///
/// <para>
/// WHY AN ENTROPY FLOOR AND NOT A COMPOSITION RULE. "One uppercase, one digit, one symbol" is
/// theatre: it accepts <c>Passw0rd!</c> - which is in every cracking dictionary ever assembled -
/// and rejects <c>gravel-thicket-cinder-9</c>, which is enormously stronger. The rule measures the
/// wrong thing. What an offline attacker actually pays is the number of guesses before they hit,
/// and that is what is estimated here.
/// </para>
///
/// <para>
/// THE FLOOR, AND THE ARITHMETIC BEHIND IT: see <see cref="MinimumEntropyBits"/>. That derivation
/// is the load-bearing part of this file; the estimator below is only the machinery for comparing
/// a password against it.
/// </para>
///
/// <para>
/// HOW THE ESTIMATE IS BUILT. Not by counting character classes and not by multiplying length by a
/// pool size - both of those score <c>qwerty123456</c> at 62 bits, which is off by roughly a factor
/// of a trillion. Instead the password is decomposed into the CHEAPEST sequence of tokens an
/// attacker could use to produce it, by shortest-path search over the string:
/// </para>
/// <list type="bullet">
///   <item><description>a hit in the small embedded ranked list of the most-tried passwords, folded
///   for case and leetspeak, costs about <c>log2(rank)</c> bits - the position in the list IS the
///   guess count;</description></item>
///   <item><description>a repeated run (<c>aaaaaa</c>, <c>abcabcabc</c>) costs the base block plus
///   <c>log2(repeat count)</c>, because repeating is one extra parameter, not extra secret;</description></item>
///   <item><description>an alphabetic or numeric run (<c>abcdef</c>, <c>987654</c>) costs about
///   <c>log2(alphabet) + log2(length)</c> - the attacker only has to guess where it starts and how
///   far it goes;</description></item>
///   <item><description>a walk across adjacent QWERTY keys (<c>qwerty</c>, <c>asdfgh</c>) is priced
///   the same way, over the keys a walk could start from;</description></item>
///   <item><description>anything the matchers do not explain is charged at the full
///   <c>log2(pool size)</c> per character, where the pool is inferred from the classes actually
///   present.</description></item>
/// </list>
/// <para>
/// The total is the MINIMUM over all decompositions, so adding a pattern to a password can never
/// raise its score - the property a composition rule fails to have, and the reason
/// <c>Password1!</c> cannot buy its way over the floor by appending punctuation.
/// </para>
///
/// <para>
/// KNOWN LIMITATIONS, STATED RATHER THAN HIDDEN. (a) The natural-language list (TD-73) is small - a few hundred
/// English and Spanish words - so a passphrase of UNCOMMON words is still scored as random letters and
/// over-estimated; only words on the list are priced as words. (b) Personal facts (a name, a birth year, the vault name) are not detected, because the
/// domain has no access to them. (c) The keyboard model is QWERTY-only and approximates key
/// adjacency by row and column proximity. All three make the estimate too GENEROUS, never too
/// harsh, which is the direction that matters for the floor being a floor: a password this type
/// accepts may be weaker than it says, so the floor is set with margin (see
/// <see cref="MinimumEntropyBits"/>) rather than at the break-even point.
/// </para>
///
/// <para>
/// NO PASSWORD IS STORED OR LOGGED. Nothing here writes to a field, a cache, a static or a log.
/// Every input is a parameter and every output is a number or an enum (<c>15-Security</c> §9). The
/// candidate is not even quoted back inside the result object.
/// </para>
///
/// <para>
/// BCL ONLY, as the whole of <c>Pine.Core</c> is (see <c>ImportKeyFactory</c> for the precedent):
/// no package reference, no downloaded corpus, no network. The word lists are an array literal
/// (<see cref="CommonPasswords"/>) and an embedded text resource (<see cref="NaturalWords"/>).
/// </para>
/// </remarks>
public static class PasswordStrengthPolicy
{
    /// <summary>
    /// The entropy floor, in bits, a master password must reach before Pine will use it to key a
    /// vault.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ARITHMETIC. This number is derived from the KDF Pine actually ships, not chosen for
    /// looking round. <see cref="KdfParameters.Argon2idV1"/> is Argon2id at
    /// <c>m = 65536 KiB (64 MiB)</c>, <c>t = 3</c>, <c>p = 4</c>, and every vault in existence uses
    /// it.
    /// </para>
    /// <para>
    /// <b>1. Cost of one guess.</b> Argon2id fills a 64 MiB block array and makes 3 passes over it.
    /// Each pass both reads and writes the array, so one guess moves at least
    /// <c>2 x 64 MiB x 3 = 384 MiB, i.e. ~0.375 GiB</c> of memory traffic, with a data-dependent
    /// (latency-bound) access pattern that no amount of parallelism removes.
    /// </para>
    /// <para>
    /// <b>2. Guesses per second on the best hardware.</b> Take a current top-end GPU at roughly
    /// <c>1000 GB/s</c> of memory bandwidth. Bandwidth alone caps it at
    /// <c>1000 / 0.375 = ~2,700 guesses/s</c>, and published Argon2id GPU results land well below
    /// that ceiling because the random indexing wastes most of the bandwidth. Round the ceiling UP
    /// to <c>3,000 guesses/s per GPU</c> to stay pessimistic. Its 24 GB of memory holds
    /// <c>24 GB / 64 MiB = 384</c> concurrent instances, so memory CAPACITY is not the binding
    /// constraint - bandwidth is, and bandwidth is what was counted.
    /// </para>
    /// <para>
    /// <b>3. The adversary.</b> Assume one far beyond anyone a personal finance vault will actually
    /// attract: a thousand such GPUs dedicated to this one vault. That is
    /// <c>3 x 10^6 = ~2^21.5 guesses/s</c>. Round up to <c>2^22</c>.
    /// </para>
    /// <para>
    /// <b>4. The time horizon.</b> Financial records stay sensitive for a lifetime, so the target is
    /// 100 years, which is <c>3.15 x 10^9 s = ~2^31.5 s</c>. The cluster therefore gets through
    /// <c>2^22 x 2^31.5 = 2^53.5</c> guesses.
    /// </para>
    /// <para>
    /// <b>5. Margin.</b> Two corrections, both upward. Hardware improves over a century: allow a
    /// <c>64x (2^6)</c> speed-up, which is deliberately conservative because Argon2id's cost is
    /// memory bandwidth, and memory bandwidth has historically improved far more slowly than
    /// compute. And an attacker expects to succeed after searching HALF the space, which is another
    /// <c>2x (2^1)</c> of required entropy.
    /// </para>
    /// <para>
    /// <b>Result:</b> <c>53.5 + 6 + 1 = 60.5</c> bits, taken as <b>60</b>. Concretely that is a
    /// five-word random passphrase (<c>5 x 12.9 = 64.5</c> bits) or ten characters drawn genuinely
    /// at random from a full keyboard (<c>10 x 6.55 = 65.5</c> bits). It is not reachable by
    /// capitalising a dictionary word and appending a year, which is the entire point.
    /// </para>
    /// <para>
    /// <b>What moving this number would mean.</b> Raising the KDF cost buys bits here at
    /// <c>log2</c> of the cost increase and nothing more: doubling Argon2id's memory to 128 MiB
    /// halves the attacker's rate and therefore lowers this floor by exactly ONE bit. That ratio is
    /// itself the argument for a high floor - the password is the cheap lever and the KDF is the
    /// expensive one. Lowering the floor requires redoing the five steps above, not an opinion.
    /// </para>
    /// </remarks>
    public const double MinimumEntropyBits = 60d;

    /// <summary>
    /// The shortest password that could possibly clear the floor, and therefore the length below
    /// which the estimator does not need to run.
    /// </summary>
    /// <remarks>
    /// DERIVED, NOT DECREED. The largest pool a US keyboard offers is 94 printable ASCII characters,
    /// worth <c>log2(94) = 6.55</c> bits each, and that is the best case even if every character is
    /// chosen by a coin flip. <c>ceil(60 / 6.55) = 10</c>. A nine-character password cannot reach
    /// 60 bits under ANY arrangement, so refusing it early is arithmetic rather than a length rule
    /// bolted on beside the entropy floor. The check exists only so a UI can say "too short"
    /// instead of the less useful "not strong enough"; it is not a second, independent gate.
    /// </remarks>
    public const int MinimumLength = 10;

    // Band boundaries for PasswordStrengthLevel. Presentation only -- MeetsFloor is the sole gate.
    // The Moderate/Strong boundary IS MinimumEntropyBits, deliberately, so a UI meter reaching
    // "Strong" and the vault accepting the password can never disagree.
    private const double VeryWeakCeilingBits = 28d;
    private const double WeakCeilingBits = 40d;
    private const double VeryStrongFloorBits = 80d;

    // Character pool sizes, by class, used for the per-character brute-force charge. These are the
    // masks a real cracking run is configured with, which is why they are CLASS sizes and not the
    // count of distinct characters the user happened to type: an attacker who knows the password is
    // lower-case-plus-digits searches all 36, not only the 7 that appear.
    private const int LowercasePoolSize = 26;
    private const int UppercasePoolSize = 26;
    private const int DigitPoolSize = 10;
    private const int AsciiSymbolPoolSize = 33; // printable ASCII minus letters and digits
    private const int NonAsciiPoolSize = 100;   // a deliberately modest charge; see BitsPerCharacter

    /// <summary>
    /// Extra bits charged for each pattern token in the chosen decomposition, modelling the fact
    /// that the attacker must also settle on WHERE each pattern sits and which family it belongs to.
    /// Kept at one bit - small on purpose, because an over-generous value would let a password made
    /// entirely of cheap patterns accumulate its way over the floor one token at a time.
    /// </summary>
    private const double PatternTokenOverheadBits = 1d;

    /// <summary>Shortest run this estimator treats as a deliberate pattern rather than coincidence.</summary>
    private const int MinimumPatternLength = 3;

    /// <summary>
    /// Evaluates a candidate master password against the entropy floor.
    /// </summary>
    /// <param name="password">
    /// The candidate. May be <c>null</c>. It is read, never retained: no field, cache or log in this
    /// type holds it after the call returns.
    /// </param>
    /// <returns>
    /// The verdict. Never <c>null</c>, and never throws for any input - including <c>null</c>, an
    /// empty string, or unpaired surrogates. A strength meter that throws while the user is
    /// mid-keystroke would be worse than no meter at all.
    /// </returns>
    public static PasswordStrengthResult Evaluate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return new PasswordStrengthResult(
                estimatedBits: 0d,
                requiredBits: MinimumEntropyBits,
                level: PasswordStrengthLevel.Unusable,
                weaknesses: [PasswordWeakness.Empty, PasswordWeakness.BelowEntropyFloor]);
        }

        var weaknesses = new List<PasswordWeakness>();

        if (password.Length < MinimumLength)
        {
            weaknesses.Add(PasswordWeakness.TooShort);
        }

        if (HasLimitedVariety(password))
        {
            weaknesses.Add(PasswordWeakness.LimitedCharacterVariety);
        }

        var bits = EstimateBits(password, weaknesses);

        if (bits < MinimumEntropyBits)
        {
            weaknesses.Add(PasswordWeakness.BelowEntropyFloor);
        }

        return new PasswordStrengthResult(bits, MinimumEntropyBits, BandFor(bits), weaknesses);
    }

    /// <summary>Maps an estimate onto its display band.</summary>
    private static PasswordStrengthLevel BandFor(double bits) => bits switch
    {
        <= 0d => PasswordStrengthLevel.Unusable,
        < VeryWeakCeilingBits => PasswordStrengthLevel.VeryWeak,
        < WeakCeilingBits => PasswordStrengthLevel.Weak,
        < MinimumEntropyBits => PasswordStrengthLevel.Moderate,
        < VeryStrongFloorBits => PasswordStrengthLevel.Strong,
        _ => PasswordStrengthLevel.VeryStrong,
    };

    /// <summary>
    /// Reports whether the password is drawn from an alphabet so narrow that the user should be told
    /// about it. Purely explanatory - never a gate; a long digits-only PIN fails on entropy, not on
    /// this.
    /// </summary>
    private static bool HasLimitedVariety(string password)
    {
        if (new HashSet<char>(password).Count < 5)
        {
            return true;
        }

        var classes = 0;

        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(static c => !char.IsLetterOrDigit(c))) classes++;

        return classes == 1;
    }

    /// <summary>
    /// Runs the shortest-path decomposition and returns the entropy of the cheapest one, appending
    /// to <paramref name="weaknesses"/> exactly the pattern families that CHOSEN decomposition used.
    /// </summary>
    /// <remarks>
    /// Reporting only the patterns on the winning path - rather than every pattern found anywhere -
    /// matters: a pattern the attacker's cheapest route does not go through did not make the
    /// password weaker, and telling the user to remove it would be advice that changes nothing.
    /// </remarks>
    private static double EstimateBits(string password, List<PasswordWeakness> weaknesses)
    {
        var length = password.Length;
        var perCharacterBits = BitsPerCharacter(password);
        var matches = FindMatches(password);

        // best[i] = cheapest entropy that produces password[0..i). previous/via record the winning
        // edge so the decomposition can be walked back for its reasons.
        var best = new double[length + 1];
        var previous = new int[length + 1];
        var via = new PasswordWeakness?[length + 1];

        Array.Fill(best, double.PositiveInfinity);
        best[0] = 0d;

        for (var start = 0; start < length; start++)
        {
            if (double.IsPositiveInfinity(best[start]))
            {
                continue;
            }

            // The always-available edge: charge one unexplained character at the full pool rate.
            Relax(start, start + 1, perCharacterBits, weakness: null);

            foreach (var match in matches)
            {
                if (match.Start == start)
                {
                    Relax(start, match.End, match.Bits + PatternTokenOverheadBits, match.Weakness);
                }
            }
        }

        for (var position = length; position > 0; position = previous[position])
        {
            if (via[position] is { } weakness && !weaknesses.Contains(weakness))
            {
                weaknesses.Add(weakness);
            }
        }

        return Math.Max(0d, best[length]);

        void Relax(int from, int to, double cost, PasswordWeakness? weakness)
        {
            var candidate = best[from] + cost;

            if (candidate < best[to])
            {
                best[to] = candidate;
                previous[to] = from;
                via[to] = weakness;
            }
        }
    }

    /// <summary>
    /// Bits charged for a character the matchers could not explain, from the pool implied by the
    /// classes present anywhere in the password.
    /// </summary>
    /// <remarks>
    /// Non-ASCII characters are charged only <see cref="NonAsciiPoolSize"/> rather than the tens of
    /// thousands of code points that exist. Deliberately: users who reach outside ASCII
    /// overwhelmingly reach for a handful of accented letters from their own keyboard layout, and
    /// pricing an e-acute as if it were drawn uniformly from Unicode would hand out roughly 17 bits
    /// for something an attacker's mask covers in a few hundred candidates.
    /// </remarks>
    private static double BitsPerCharacter(string password)
    {
        var pool = 0;

        if (password.Any(char.IsLower)) pool += LowercasePoolSize;
        if (password.Any(char.IsUpper)) pool += UppercasePoolSize;
        if (password.Any(char.IsDigit)) pool += DigitPoolSize;
        if (password.Any(static c => c < 128 && !char.IsLetterOrDigit(c))) pool += AsciiSymbolPoolSize;
        if (password.Any(static c => c >= 128)) pool += NonAsciiPoolSize;

        return pool <= 1 ? 0d : Math.Log2(pool);
    }

    /// <summary>Collects every cheap-pattern match the estimator knows how to price.</summary>
    private static List<PatternMatch> FindMatches(string password)
    {
        var matches = new List<PatternMatch>();

        AddCommonPasswordMatches(password, matches);
        AddNaturalWordMatches(password, matches);
        AddRepeatMatches(password, matches);
        AddSequenceMatches(password, matches);
        AddKeyboardMatches(password, matches);

        return matches;
    }

    /// <summary>
    /// Matches substrings against the ranked list, after folding case and leetspeak so that
    /// <c>P@ssw0rd</c> costs what <c>password</c> costs plus the couple of bits its disguise is
    /// worth.
    /// </summary>
    private static void AddCommonPasswordMatches(string password, List<PatternMatch> matches)
    {
        // The most a dictionary hit can ever cost, so a heavily-disguised hit can never be priced
        // above simply brute-forcing a token of the same length would be.
        var hitCeilingBits = Math.Log2(CommonPasswords.Count + 1) + 8d;

        for (var start = 0; start < password.Length; start++)
        {
            var widest = Math.Min(CommonPasswords.MaxLength, password.Length - start);

            for (var length = widest; length >= MinimumPatternLength; length--)
            {
                var candidate = password.Substring(start, length);
                var folded = FoldLeet(candidate.ToLowerInvariant(), out var leetApplied);
                var rank = CommonPasswords.RankOf(folded);

                if (rank < 0)
                {
                    continue;
                }

                // log2(rank + 1) is the work a ranked-dictionary walk spends before it reaches this
                // entry -- position in the list IS the guess count. Casing and leetspeak are then
                // priced as the extra parameters they are, because a serious attacker applies both
                // as rule sets over the same list rather than as additional list entries.
                var bits = Math.Log2(rank + 1) + CasingBits(candidate) + (leetApplied ? 1d : 0d);

                matches.Add(new PatternMatch(
                    start,
                    start + length,
                    Math.Min(bits, hitCeilingBits),
                    PasswordWeakness.CommonPassword));
            }
        }
    }

    /// <summary>The separators a passphrase conventionally puts between words.</summary>
    private const string WordSeparators = " -_.,;:+/";

    /// <summary>
    /// Bits for the choice of a separator after a word: a handful of conventional characters, so it
    /// is priced as that handful rather than as one draw from the whole symbol pool.
    /// </summary>
    private const double WordSeparatorBits = 3d;

    /// <summary>
    /// Matches ordinary English/Spanish words from the embedded <see cref="NaturalWords"/> list, so a
    /// passphrase is priced as the few WORDS it is rather than as the many letters it contains (TD-73).
    /// </summary>
    /// <remarks>
    /// A hit costs <c>log2(rank + 1)</c> plus the casing and leetspeak surcharges, exactly as a
    /// common password does. A word followed by one conventional separator is also offered as a single
    /// token costing the word plus <see cref="WordSeparatorBits"/>, which is what keeps
    /// <c>correct horse battery staple</c> from paying a full symbol-pool character for each space.
    /// These are only EXTRA edges in the shortest-path search, so they can lower a score and never
    /// raise one: the change fails toward lower strength by construction.
    /// </remarks>
    private static void AddNaturalWordMatches(string password, List<PatternMatch> matches)
    {
        for (var start = 0; start < password.Length; start++)
        {
            var widest = Math.Min(NaturalWords.MaxLength, password.Length - start);

            for (var length = widest; length >= NaturalWords.MinimumWordLength; length--)
            {
                var candidate = password.Substring(start, length);
                var folded = FoldLeet(FoldAccents(candidate.ToLowerInvariant()), out var leetApplied);
                var rank = NaturalWords.RankOf(folded);

                if (rank < 0)
                {
                    continue;
                }

                var bits = Math.Log2(rank + 1) + CasingBits(candidate) + (leetApplied ? 1d : 0d);
                var end = start + length;

                matches.Add(new PatternMatch(start, end, bits, PasswordWeakness.DictionaryWord));

                if (end < password.Length && WordSeparators.Contains(password[end]))
                {
                    matches.Add(new PatternMatch(start, end + 1, bits + WordSeparatorBits, PasswordWeakness.DictionaryWord));
                }
            }
        }
    }

    /// <summary>Maps the common Spanish accented letters to their base letter, one character to one.</summary>
    private static string FoldAccents(string lowered)
    {
        char[]? buffer = null;

        for (var index = 0; index < lowered.Length; index++)
        {
            var replacement = lowered[index] switch
            {
                'á' => 'a',
                'é' => 'e',
                'í' => 'i',
                'ó' => 'o',
                'ú' or 'ü' => 'u',
                'ñ' => 'n',
                _ => ' ',
            };

            if (replacement != ' ')
            {
                buffer ??= lowered.ToCharArray();
                buffer[index] = replacement;
            }
        }

        return buffer is null ? lowered : new string(buffer);
    }

    /// <summary>
    /// Bits for the capitalisation of a dictionary hit. All-lower is free (it is the list's own
    /// form); the two conventional shapes cost a bit; anything else is charged per character up to a
    /// cap, since an attacker's case-mangling rules cover the conventional shapes cheaply and
    /// arbitrary internal casing much less so.
    /// </summary>
    private static double CasingBits(string token)
    {
        if (!token.Any(char.IsUpper))
        {
            return 0d;
        }

        var allUpper = token.Where(char.IsLetter).All(char.IsUpper);
        var firstOnly = char.IsUpper(token[0]) && !token.Skip(1).Any(char.IsUpper);

        return allUpper || firstOnly ? 1d : Math.Min(token.Length, 6);
    }

    /// <summary>
    /// Replaces the standard leetspeak substitutions so a disguised dictionary word still matches.
    /// </summary>
    /// <remarks>
    /// A single mapping per symbol, not every plausible one: <c>1</c> becomes <c>i</c> and never
    /// <c>l</c>. Trying both would multiply the work at every position for a gain the ranked-list
    /// cost already dominates, and the failure mode of a miss is that the token is scored as random
    /// - the generous direction the floor's margin already accounts for.
    /// </remarks>
    private static string FoldLeet(string token, out bool applied)
    {
        applied = false;
        char[]? buffer = null;

        for (var index = 0; index < token.Length; index++)
        {
            var replacement = token[index] switch
            {
                '0' => 'o',
                '1' => 'i',
                '3' => 'e',
                '4' => 'a',
                '5' => 's',
                '7' => 't',
                '8' => 'b',
                '9' => 'g',
                '@' => 'a',
                '$' => 's',
                '!' => 'i',
                '|' => 'l',
                _ => '\0',
            };

            if (replacement == '\0')
            {
                continue;
            }

            buffer ??= token.ToCharArray();
            buffer[index] = replacement;
            applied = true;
        }

        return buffer is null ? token : new string(buffer);
    }

    /// <summary>
    /// Matches a block repeated back to back - <c>aaaa</c>, <c>abcabcabc</c>, <c>lolololol</c>.
    /// </summary>
    /// <remarks>
    /// Priced as the cost of the base block plus <c>log2(repeat count)</c>: repeating is one extra
    /// number for the attacker to guess, not extra secret. The base block itself is charged at the
    /// brute-force rate rather than decomposed recursively - recursion would be more accurate and
    /// would only ever LOWER the score, so skipping it errs in the generous direction and keeps the
    /// estimator fast enough to run on every keystroke.
    /// </remarks>
    private static void AddRepeatMatches(string password, List<PatternMatch> matches)
    {
        var perCharacterBits = BitsPerCharacter(password);

        for (var start = 0; start < password.Length; start++)
        {
            var remaining = password.Length - start;

            for (var blockLength = 1; blockLength <= remaining / 2; blockLength++)
            {
                var repeats = 1;

                while (((repeats + 1) * blockLength) <= remaining &&
                       string.CompareOrdinal(
                           password, start,
                           password, start + (repeats * blockLength),
                           blockLength) == 0)
                {
                    repeats++;

                    var length = repeats * blockLength;

                    if (length < MinimumPatternLength)
                    {
                        continue;
                    }

                    matches.Add(new PatternMatch(
                        start,
                        start + length,
                        (blockLength * perCharacterBits) + Math.Log2(repeats),
                        PasswordWeakness.RepeatedPattern));
                }
            }
        }
    }

    /// <summary>
    /// Matches runs where consecutive characters step by exactly one within the same alphabet:
    /// <c>abcdef</c>, <c>123456</c>, <c>987</c>, <c>ZYX</c>.
    /// </summary>
    /// <remarks>
    /// Priced as <c>log2(alphabet size)</c> for the starting character plus <c>log2(length)</c> for
    /// how far it runs, plus one bit if it descends - descending runs are rarer, so an attacker
    /// tries ascending first.
    /// </remarks>
    private static void AddSequenceMatches(string password, List<PatternMatch> matches)
    {
        for (var start = 0; start + MinimumPatternLength <= password.Length; start++)
        {
            var alphabetSize = AlphabetSizeOf(password[start]);

            if (alphabetSize == 0 || AlphabetSizeOf(password[start + 1]) != alphabetSize)
            {
                continue;
            }

            var delta = password[start + 1] - password[start];

            if (delta is not 1 and not -1)
            {
                continue;
            }

            var end = start + 1;

            while (end + 1 < password.Length &&
                   AlphabetSizeOf(password[end + 1]) == alphabetSize &&
                   password[end + 1] - password[end] == delta)
            {
                end++;
            }

            for (var last = start + MinimumPatternLength - 1; last <= end; last++)
            {
                var length = last - start + 1;

                matches.Add(new PatternMatch(
                    start,
                    last + 1,
                    Math.Log2(alphabetSize) + Math.Log2(length) + (delta < 0 ? 1d : 0d),
                    PasswordWeakness.SequentialRun));
            }
        }
    }

    /// <summary>Size of the alphabet a run character belongs to, or 0 if it is not in one.</summary>
    private static int AlphabetSizeOf(char value)
    {
        if (value is >= 'a' and <= 'z') return LowercasePoolSize;
        if (value is >= 'A' and <= 'Z') return UppercasePoolSize;
        if (value is >= '0' and <= '9') return DigitPoolSize;

        return 0;
    }

    /// <summary>
    /// Matches a walk across physically adjacent keys on a US QWERTY keyboard - <c>qwerty</c>,
    /// <c>asdfgh</c>, <c>1qaz2wsx</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The adjacency model is deliberately cheap: keys are placed on a row/column grid built from
    /// the four literal keyboard rows, and two keys are adjacent when they are side by side in one
    /// row, or in neighbouring rows within one column of each other. That approximates the physical
    /// stagger rather than reproducing it, and it costs a generated dictionary of about ninety
    /// entries instead of a hand-maintained neighbour graph nobody would keep correct.
    /// </para>
    /// <para>
    /// It is QWERTY-only. A user on AZERTY or Dvorak gets their keyboard walks scored as random
    /// characters - generous, like every other limitation here, which is what the floor's margin is
    /// for.
    /// </para>
    /// </remarks>
    private static void AddKeyboardMatches(string password, List<PatternMatch> matches)
    {
        // log2 of the number of keys a walk could begin from: the whole main block of the keyboard.
        var startingKeyBits = Math.Log2(KeyPositions.Count);

        for (var start = 0; start + MinimumPatternLength <= password.Length; start++)
        {
            if (!KeyPositions.ContainsKey(password[start]))
            {
                continue;
            }

            var end = start;

            while (end + 1 < password.Length && AreAdjacentKeys(password[end], password[end + 1]))
            {
                end++;
            }

            for (var last = start + MinimumPatternLength - 1; last <= end; last++)
            {
                var tokenLength = last - start + 1;
                var shifted = 0;

                for (var index = start; index <= last; index++)
                {
                    if (ShiftedKeys.Contains(password[index]))
                    {
                        shifted++;
                    }
                }

                // One extra bit per shifted key, capped: holding shift for PART of a walk is a
                // choice the attacker has to reconstruct, but a walk typed entirely with shift down
                // is one choice, not one per character.
                var shiftBits = shifted == 0 || shifted == tokenLength ? 0d : Math.Min(shifted, 4);

                matches.Add(new PatternMatch(
                    start,
                    last + 1,
                    startingKeyBits + Math.Log2(tokenLength) + shiftBits,
                    PasswordWeakness.KeyboardPattern));
            }
        }
    }

    /// <summary>Whether two characters sit on neighbouring keys under the grid model above.</summary>
    private static bool AreAdjacentKeys(char first, char second)
    {
        if (first == second ||
            !KeyPositions.TryGetValue(first, out var a) ||
            !KeyPositions.TryGetValue(second, out var b))
        {
            return false;
        }

        var rowDistance = Math.Abs(a.Row - b.Row);
        var columnDistance = Math.Abs(a.Column - b.Column);

        return rowDistance switch
        {
            0 => columnDistance == 1,
            1 => columnDistance <= 1,
            _ => false,
        };
    }

    /// <summary>The four main rows of a US QWERTY keyboard, unshifted.</summary>
    private static readonly string[] UnshiftedRows =
    [
        "`1234567890-=",
        "qwertyuiop[]\\",
        "asdfghjkl;'",
        "zxcvbnm,./",
    ];

    /// <summary>The same four rows with shift held, so a shifted walk is still recognised as a walk.</summary>
    private static readonly string[] ShiftedRows =
    [
        "~!@#$%^&*()_+",
        "QWERTYUIOP{}|",
        "ASDFGHJKL:\"",
        "ZXCVBNM<>?",
    ];

    private static readonly Dictionary<char, (int Row, int Column)> KeyPositions = BuildKeyPositions();

    private static readonly HashSet<char> ShiftedKeys = [.. ShiftedRows.SelectMany(static row => row)];

    private static Dictionary<char, (int Row, int Column)> BuildKeyPositions()
    {
        var positions = new Dictionary<char, (int Row, int Column)>();

        for (var row = 0; row < UnshiftedRows.Length; row++)
        {
            for (var column = 0; column < UnshiftedRows[row].Length; column++)
            {
                positions[UnshiftedRows[row][column]] = (row, column);
            }

            for (var column = 0; column < ShiftedRows[row].Length; column++)
            {
                positions[ShiftedRows[row][column]] = (row, column);
            }
        }

        return positions;
    }

    /// <summary>
    /// One priced explanation for a slice of the password: the half-open range it covers, what the
    /// attacker pays for it, and which reason to report if the winning decomposition uses it.
    /// </summary>
    private readonly record struct PatternMatch(int Start, int End, double Bits, PasswordWeakness Weakness);
}
