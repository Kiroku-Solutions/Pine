using System;
using System.Text;

namespace Pine.Infrastructure.Diagnostics;

/// <summary>
/// THE BOUNDARY. Every string that leaves the process into a log file passes through here first:
/// the formatted message, the exception's message, every stack frame, and every context value.
/// </summary>
/// <remarks>
/// <para>
/// WHY HAND-ROLLED SCANNING AND NOT <c>Regex</c>. This code runs on the failure path -- including
/// the crash path, where the process is already dying -- so it must be bounded and it must never be
/// the thing that hangs. Every rule below is a single forward pass with no backtracking, so the
/// whole redactor is O(n) in the length of its input with a constant factor of four passes, and no
/// input can make it quadratic. A regular expression with alternation and repetition offers no such
/// guarantee.
/// </para>
/// <para>
/// WHY IT NEVER THROWS. A logger that can throw turns a recoverable anomaly into a crash, and this
/// method is called from inside catch blocks. On any unexpected failure it returns
/// <see cref="RedactionFailed"/> -- deliberately NOT the original string, because the failure mode
/// of a redactor must be "say nothing", never "say everything".
/// </para>
/// <para>
/// WHAT IT DOES NOT DO. It is a net, not a proof. It cannot recognise a payee name or a category as
/// such, which is why <c>30-Observability</c> §4.3's first two clauses are also enforced by NOT
/// LOGGING those values at all (see the deleted OCR content trace in <c>PaddleOcrEngine</c>); this
/// class is the second line of defence for values that reach a message by accident.
/// </para>
/// <para>
/// EVERYTHING IS INVARIANT AND ORDINAL. No rule consults the current culture, so the same input
/// redacts identically on every machine and in every test run.
/// </para>
/// </remarks>
public static class LogRedactor
{
    /// <summary>What is returned when redaction itself fails. Never the unredacted input.</summary>
    public const string RedactionFailed = "<redaction-failed>";

    /// <summary>Replacement for an absolute filesystem path. Implements §4.3, third clause.</summary>
    public const string PathPlaceholder = "<path>";

    /// <summary>
    /// Replacement for a path under the user's profile directory. Implements §4.3, third clause and
    /// the profile-name half of its fourth.
    /// </summary>
    public const string HomePlaceholder = "<home>";

    /// <summary>Replacement for an e-mail address. Implements §4.3, first clause (PII).</summary>
    public const string EmailPlaceholder = "<email>";

    /// <summary>Replacement for a long digit run. Implements §4.3, first and fourth clauses.</summary>
    public const string DigitsPlaceholder = "<digits>";

    /// <summary>Replacement for a money-shaped token. Implements §4.3, second clause (amounts).</summary>
    public const string AmountPlaceholder = "<amount>";

    /// <summary>A digit run of this length or longer is redacted.</summary>
    private const int DigitRunThreshold = 6;

    /// <summary>
    /// The user's profile directory, resolved once. Empty when the platform does not report a
    /// usable one, in which case the <see cref="HomePlaceholder"/> rule is skipped rather than
    /// matching everything.
    /// </summary>
    private static readonly string UserProfileDirectory = ResolveUserProfileDirectory();

    /// <summary>
    /// Redacts one string for inclusion in a log record.
    /// </summary>
    /// <param name="value">The raw string. May be <see langword="null"/>.</param>
    /// <returns>
    /// The redacted string, <see langword="null"/> when <paramref name="value"/> was null, or
    /// <see cref="RedactionFailed"/> if redaction threw.
    /// </returns>
    /// <remarks>
    /// PASS ORDER IS LOAD-BEARING. Paths run first so a Windows drive letter and a stack frame's
    /// trailing <c>:line 66</c> are consumed as part of the path rather than surviving as stray
    /// tokens; e-mail runs before the numeric rules so an address containing digits is replaced
    /// whole; and the money rule runs before the digit-run rule so that <c>1234567.89</c> is
    /// reported as an amount rather than as digits with a decimal tail left behind. Each
    /// placeholder contains no digit, no <c>@</c> and no path separator, so no pass can ever match
    /// inside the output of an earlier one.
    /// </remarks>
    public static string? Redact(string? value)
    {
        if (value is null || value.Length == 0)
        {
            return value;
        }

        try
        {
            var redacted = RedactPaths(value);
            redacted = RedactEmails(redacted);
            redacted = RedactAmounts(redacted);
            redacted = RedactDigitRuns(redacted);

            return redacted;
        }
        catch (Exception)
        {
            // See the class remarks: the failure mode of a redactor is "say nothing".
            return RedactionFailed;
        }
    }

    /// <summary>
    /// Implements <c>30-Observability</c> §4.3, third clause ("a log MUST NOT include a path on the
    /// user's filesystem") and, for a path under the profile directory, the fourth clause's ban on
    /// the profile name.
    /// </summary>
    /// <remarks>
    /// Three shapes are recognised: a drive-qualified Windows path (<c>C:\...</c> or <c>C:/...</c>),
    /// a UNC share (<c>\\server\share\...</c>), and a POSIX absolute path. The POSIX rule is the one
    /// that could plausibly fire on prose, so it is deliberately narrow: the leading slash must not
    /// follow a letter or digit (which rules out <c>and/or</c>) and the run must contain at least
    /// two separators (which rules out a bare <c>/tmp</c> mid-sentence). A path that starts with the
    /// user's profile directory becomes <see cref="HomePlaceholder"/> rather than
    /// <see cref="PathPlaceholder"/>, because the fact that a file sat under the user's home is the
    /// part that names them.
    /// </remarks>
    private static string RedactPaths(string value)
    {
        StringBuilder? builder = null;
        var index = 0;
        var copiedUpTo = 0;

        while (index < value.Length)
        {
            var length = MatchPath(value, index);

            if (length <= 0)
            {
                index++;
                continue;
            }

            builder ??= new StringBuilder(value.Length);
            builder.Append(value, copiedUpTo, index - copiedUpTo);
            builder.Append(IsUnderUserProfile(value, index, length) ? HomePlaceholder : PathPlaceholder);

            index += length;
            copiedUpTo = index;
        }

        return Finish(builder, value, copiedUpTo);
    }

    /// <summary>Returns the length of the path token starting at <paramref name="start"/>, or zero.</summary>
    private static int MatchPath(string value, int start)
    {
        int cursor;
        var separators = 1;

        if (IsDriveQualified(value, start))
        {
            cursor = start + 3;
        }
        else if (IsUncPrefix(value, start))
        {
            cursor = start + 2;
        }
        else if (IsPosixPathStart(value, start))
        {
            cursor = start + 1;
        }
        else
        {
            return 0;
        }

        while (cursor < value.Length && !IsPathTerminator(value[cursor]))
        {
            if (value[cursor] is '\\' or '/')
            {
                separators++;
            }

            cursor++;
        }

        // Trailing sentence punctuation belongs to the prose, not to the path.
        while (cursor > start && IsTrailingPunctuation(value[cursor - 1]))
        {
            cursor--;
        }

        var length = cursor - start;

        if (length <= 0)
        {
            return 0;
        }

        // A drive letter or a UNC prefix is unambiguous on its own; a lone POSIX slash is not, so it
        // needs a second separator before the run counts as a path.
        return value[start] == '/' && separators < 2 ? 0 : length;
    }

    private static bool IsTrailingPunctuation(char candidate)
        => candidate is '.' or ',' or ';' or ')' or ']';

    private static bool IsDriveQualified(string value, int start)
        => start + 2 < value.Length
           && char.IsAsciiLetter(value[start])
           && value[start + 1] == ':'
           && (value[start + 2] == '\\' || value[start + 2] == '/')
           && (start == 0 || !char.IsAsciiLetterOrDigit(value[start - 1]));

    private static bool IsUncPrefix(string value, int start)
        => start + 2 < value.Length
           && value[start] == '\\'
           && value[start + 1] == '\\'
           && !IsPathTerminator(value[start + 2]);

    private static bool IsPosixPathStart(string value, int start)
        => value[start] == '/'
           && start + 1 < value.Length
           && value[start + 1] != '/'
           && !IsPathTerminator(value[start + 1])
           && (start == 0 || !char.IsAsciiLetterOrDigit(value[start - 1]));

    /// <summary>Characters that can never appear inside a path token in a log message.</summary>
    private static bool IsPathTerminator(char candidate)
        => char.IsWhiteSpace(candidate)
           || candidate is '"' or '<' or '>' or '|' or '*' or '?' or '\0'
           || candidate == '\'';

    private static bool IsUnderUserProfile(string value, int start, int length)
    {
        if (UserProfileDirectory.Length == 0 || length < UserProfileDirectory.Length)
        {
            return false;
        }

        return string.Compare(
            value,
            start,
            UserProfileDirectory,
            0,
            UserProfileDirectory.Length,
            StringComparison.OrdinalIgnoreCase) == 0;
    }

    /// <summary>
    /// Implements <c>30-Observability</c> §4.3, first clause: an e-mail address is PII, and
    /// <c>15-Security</c> §3 names it as such explicitly.
    /// </summary>
    /// <remarks>
    /// The scan is anchored on <c>@</c> and walks outwards, which keeps it linear: each character
    /// is examined at most twice -- once by the forward scan, once by the walk back over the local
    /// part -- and there is no alternation to backtrack through. A domain must contain a dot with at
    /// least two trailing characters, so <c>@here</c> in prose is not an address.
    /// </remarks>
    private static string RedactEmails(string value)
    {
        StringBuilder? builder = null;
        var copiedUpTo = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '@')
            {
                continue;
            }

            var localStart = index;
            while (localStart > copiedUpTo && IsEmailLocalChar(value[localStart - 1]))
            {
                localStart--;
            }

            if (localStart == index)
            {
                continue;
            }

            var domainEnd = index + 1;

            while (domainEnd < value.Length && IsEmailDomainChar(value[domainEnd]))
            {
                domainEnd++;
            }

            // A trailing dot is sentence punctuation, not part of the domain.
            while (domainEnd > index + 1 && value[domainEnd - 1] == '.')
            {
                domainEnd--;
            }

            // The last dot INSIDE the trimmed domain; found by walking back rather than recorded on
            // the way forward, because trimming the sentence punctuation can discard the dot the
            // forward scan had recorded ("a@b.com." must still redact, on the dot before "com").
            var dotIndex = domainEnd - 1;
            while (dotIndex > index && value[dotIndex] != '.')
            {
                dotIndex--;
            }

            if (dotIndex <= index || domainEnd - dotIndex < 3)
            {
                continue;
            }

            builder ??= new StringBuilder(value.Length);
            builder.Append(value, copiedUpTo, localStart - copiedUpTo);
            builder.Append(EmailPlaceholder);

            copiedUpTo = domainEnd;
            index = domainEnd - 1;
        }

        return Finish(builder, value, copiedUpTo);
    }

    private static bool IsEmailLocalChar(char candidate)
        => char.IsAsciiLetterOrDigit(candidate) || candidate is '.' or '_' or '-' or '+';

    private static bool IsEmailDomainChar(char candidate)
        => char.IsAsciiLetterOrDigit(candidate) || candidate is '.' or '-';

    /// <summary>
    /// Implements <c>30-Observability</c> §4.3, second clause: a log must not carry a raw
    /// transaction amount.
    /// </summary>
    /// <remarks>
    /// The shape is <c>nnn[.,]nn</c> -- one or more digits, a decimal separator, then exactly two
    /// more digits -- which is what a receipt total, a line-item price and an account balance all
    /// look like once formatted. It is anchored so that a version number (<c>1.2.3</c>), a duration
    /// (<c>12.345</c>) and a date (<c>2026-08-28</c>) do not match: the fractional part must be
    /// exactly two digits, must not be followed by another digit or separator, and the integer part
    /// must not itself continue a dotted or hyphenated token.
    /// </remarks>
    private static string RedactAmounts(string value)
    {
        StringBuilder? builder = null;
        var copiedUpTo = 0;
        var index = 0;

        while (index < value.Length)
        {
            if (!char.IsAsciiDigit(value[index]))
            {
                index++;
                continue;
            }

            var integerEnd = index;
            while (integerEnd < value.Length && char.IsAsciiDigit(value[integerEnd]))
            {
                integerEnd++;
            }

            var isAmount = (index == 0 || !IsAmountBoundaryBreaker(value[index - 1]))
                           && integerEnd + 2 < value.Length
                           && value[integerEnd] is '.' or ','
                           && char.IsAsciiDigit(value[integerEnd + 1])
                           && char.IsAsciiDigit(value[integerEnd + 2])
                           && (integerEnd + 3 >= value.Length
                               || (!char.IsAsciiDigit(value[integerEnd + 3])
                                   && value[integerEnd + 3] is not ('.' or ',')));

            if (!isAmount)
            {
                index = integerEnd + 1;
                continue;
            }

            builder ??= new StringBuilder(value.Length);
            builder.Append(value, copiedUpTo, index - copiedUpTo);
            builder.Append(AmountPlaceholder);

            index = integerEnd + 3;
            copiedUpTo = index;
        }

        return Finish(builder, value, copiedUpTo);
    }

    /// <summary>A digit preceded by one of these is a fragment of a larger token, not an amount.</summary>
    private static bool IsAmountBoundaryBreaker(char candidate)
        => candidate is '.' or ',' or '-' or ':' or '/';

    /// <summary>
    /// Implements <c>30-Observability</c> §4.3, first and fourth clauses: a run of six or more
    /// digits has the shape of an account number, a card number, a tax id or a phone number, none of
    /// which may reach a log.
    /// </summary>
    /// <remarks>
    /// Six is the threshold because it is the shortest run that is not plausibly a byte count, a
    /// line number, a millisecond duration or a status code -- that is, the counters this sink
    /// exists to record. A lower threshold would redact the diagnostics along with the secrets.
    /// </remarks>
    private static string RedactDigitRuns(string value)
    {
        StringBuilder? builder = null;
        var copiedUpTo = 0;
        var index = 0;

        while (index < value.Length)
        {
            if (!char.IsAsciiDigit(value[index]))
            {
                index++;
                continue;
            }

            var runEnd = index;
            while (runEnd < value.Length && char.IsAsciiDigit(value[runEnd]))
            {
                runEnd++;
            }

            if (runEnd - index >= DigitRunThreshold)
            {
                builder ??= new StringBuilder(value.Length);
                builder.Append(value, copiedUpTo, index - copiedUpTo);
                builder.Append(DigitsPlaceholder);
                copiedUpTo = runEnd;
            }

            index = runEnd;
        }

        return Finish(builder, value, copiedUpTo);
    }

    /// <summary>
    /// Completes a pass: returns the original string untouched when nothing matched (the common
    /// case, and the one worth not allocating for), or the builder plus the unexamined tail.
    /// </summary>
    private static string Finish(StringBuilder? builder, string value, int copiedUpTo)
    {
        if (builder is null)
        {
            return value;
        }

        builder.Append(value, copiedUpTo, value.Length - copiedUpTo);

        return builder.ToString();
    }

    /// <summary>
    /// Resolves the user's profile directory, tolerating a platform that reports none.
    /// </summary>
    /// <remarks>
    /// A very short result is discarded: matching against a one- or two-character "home" would turn
    /// the <see cref="HomePlaceholder"/> rule into a wildcard over every path on the machine.
    /// </remarks>
    private static string ResolveUserProfileDirectory()
    {
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return profile.Length >= 4 ? profile.TrimEnd('\\', '/') : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
