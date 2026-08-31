# Receipt Trust Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the receipt pipeline stop asserting three things it has not measured — that a scan is new, that a date is unambiguous, and that an extraction is 90% confident.

**Architecture:** Three seams, all in `Pine.Core` so they are testable without an OCR engine: `ReceiptDateParser` (explicit formats, culture-ordered, ambiguity-flagging), a per-field confidence model built from the `OcrWord.Confidence` values PaddleOCR already produces and the extractor currently discards, and a new `s1:` receipt scheme on the existing `ImportKeyFactory` that reuses the import path's already-indexed duplicate lookup. `OnnxNlpFieldExtractor` and `ScanReceiptViewModel` consume them; nothing new decides anything on the user's behalf.

**Tech Stack:** .NET 10, xUnit 2.9.3, FluentAssertions 8.10.0, NSubstitute 5.3.0, EF Core + SQLCipher, Avalonia MVVM (CommunityToolkit source-generated properties), MediatR.

**Spec:** `docs/superpowers/specs/2026-08-30-receipt-trust-design.md`

## Global Constraints

- `Pine.Core` takes **no** package or project references. BCL only. Both new types live there and must honour that.
- Every culture-sensitive conversion pins its culture explicitly. `CultureInfo.CurrentCulture` is read in exactly one place (`ReceiptDateParser`) and is always passable as a parameter so tests never depend on the machine's locale.
- Tests are xUnit + FluentAssertions, in `tests/Pine.Tests.Unit` (no database) or `tests/Pine.Tests.Integration` (real vault).
- Run the unit suite with `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj` and the integration suite with `dotnet test tests/Pine.Tests.Integration/Pine.Tests.Integration.csproj`.
- Do not touch TD-99 (budgets screen), TD-97 (flaky suite) or TD-98 (unhandled concurrency conflict). They are separate rows.
- Confidence band thresholds, fixed once here and used verbatim everywhere: `High >= 0.90`, `Medium >= 0.70`, otherwise `Low`; `NotFound` is a distinct band, not a score of zero.
- The receipt key scheme prefix is exactly `s1:`.

---

### Task 1: `ReceiptDateParser` — explicit formats, culture order, ambiguity flag

**Files:**
- Create: `src/Pine.Core/Ai/ReceiptDateParser.cs`
- Test: `tests/Pine.Tests.Unit/Ai/ReceiptDateParserTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:

```csharp
namespace Pine.Core.Ai;

public readonly record struct ReceiptDateCandidate(
    DateTimeOffset Value,
    bool IsAmbiguous,
    DateTimeOffset? Alternative);

public static class ReceiptDateParser
{
    public static bool PrefersDayFirst(CultureInfo culture);
    public static ReceiptDateCandidate? Parse(string text, CultureInfo? culture = null, DateTimeOffset? now = null);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Globalization;
using FluentAssertions;
using Pine.Core.Ai;
using Xunit;

namespace Pine.Tests.Unit.Ai;

public class ReceiptDateParserTests
{
    private static readonly CultureInfo DayFirst = new("es-EC");
    private static readonly CultureInfo MonthFirst = new("en-US");
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_day_first_culture_reads_an_ambiguous_date_day_first()
    {
        var candidate = ReceiptDateParser.Parse("FECHA: 05/03/2026", DayFirst, Now);

        candidate.Should().NotBeNull();
        candidate!.Value.Value.Month.Should().Be(3);
        candidate.Value.Value.Day.Should().Be(5);
    }

    [Fact]
    public void A_month_first_culture_reads_the_same_string_month_first()
    {
        var candidate = ReceiptDateParser.Parse("DATE 05/03/2026", MonthFirst, Now);

        candidate!.Value.Value.Month.Should().Be(5);
        candidate.Value.Value.Day.Should().Be(3);
    }

    [Fact]
    public void An_ambiguous_date_carries_the_other_reading()
    {
        var candidate = ReceiptDateParser.Parse("05/03/2026", DayFirst, Now);

        candidate!.Value.IsAmbiguous.Should().BeTrue();
        candidate.Value.Alternative.Should().NotBeNull();
        candidate.Value.Alternative!.Value.Month.Should().Be(5);
        candidate.Value.Alternative!.Value.Day.Should().Be(3);
    }

    [Fact]
    public void A_day_above_twelve_is_not_ambiguous()
    {
        var candidate = ReceiptDateParser.Parse("23/03/2026", DayFirst, Now);

        candidate!.Value.IsAmbiguous.Should().BeFalse();
        candidate.Value.Alternative.Should().BeNull();
        candidate.Value.Value.Day.Should().Be(23);
    }

    [Fact]
    public void An_iso_date_is_read_as_iso_whatever_the_culture()
    {
        var candidate = ReceiptDateParser.Parse("2026-03-05", MonthFirst, Now);

        candidate!.Value.Value.Month.Should().Be(3);
        candidate.Value.Value.Day.Should().Be(5);
        candidate.Value.IsAmbiguous.Should().BeFalse();
    }

    [Fact]
    public void Single_digit_components_are_accepted()
    {
        var candidate = ReceiptDateParser.Parse("5/3/2026", DayFirst, Now);

        candidate.Should().NotBeNull();
        candidate!.Value.Value.Day.Should().Be(5);
    }

    [Fact]
    public void A_two_digit_year_is_accepted()
    {
        var candidate = ReceiptDateParser.Parse("05/03/26", DayFirst, Now);

        candidate!.Value.Value.Year.Should().Be(2026);
    }

    [Fact]
    public void A_dot_separator_is_accepted()
    {
        var candidate = ReceiptDateParser.Parse("05.03.2026", DayFirst, Now);

        candidate.Should().NotBeNull();
    }

    [Fact]
    public void An_implausible_year_is_rejected_rather_than_returned()
    {
        ReceiptDateParser.Parse("05/03/1998", DayFirst, Now).Should().BeNull();
    }

    [Fact]
    public void The_alternative_is_dropped_when_it_is_implausible()
    {
        // 13 cannot be a month, so 13/07/2026 has exactly one reading.
        var candidate = ReceiptDateParser.Parse("13/07/2026", DayFirst, Now);

        candidate!.Value.IsAmbiguous.Should().BeFalse();
        candidate.Value.Alternative.Should().BeNull();
    }

    [Fact]
    public void Text_with_no_date_returns_null()
    {
        ReceiptDateParser.Parse("TOTAL 24.50", DayFirst, Now).Should().BeNull();
    }

    [Theory]
    [InlineData("es-EC", true)]
    [InlineData("en-GB", true)]
    [InlineData("en-US", false)]
    public void The_culture_decides_the_component_order(string cultureName, bool dayFirst)
    {
        ReceiptDateParser.PrefersDayFirst(new CultureInfo(cultureName)).Should().Be(dayFirst);
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj --filter FullyQualifiedName~ReceiptDateParserTests`
Expected: FAIL — `ReceiptDateParser` does not exist (compile error).

- [ ] **Step 3: Implement `ReceiptDateParser`**

```csharp
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Pine.Core.Ai;

/// <summary>
/// One reading of a date printed on a receipt, together with the other reading when the printed
/// string admits two.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE ALTERNATIVE IS CARRIED RATHER THAN RESOLVED (TD-21). <c>05/03/2026</c> is 5 March in
/// Quito and 3 May in Chicago, and no amount of parsing skill can tell which one the paper meant.
/// The previous code answered anyway — it tried <see cref="CultureInfo.InvariantCulture"/> first,
/// which IS month-first, so its <c>es-EC</c> leg was unreachable for exactly the strings it existed
/// to handle, and the transaction landed in the wrong month's budget with no trace. Returning both
/// readings moves that decision to the one party who can actually make it: the person holding the
/// receipt.
/// </para>
/// </remarks>
public readonly record struct ReceiptDateCandidate(
    DateTimeOffset Value,
    bool IsAmbiguous,
    DateTimeOffset? Alternative);

/// <summary>
/// Parses the date out of a line of receipt text using an explicit, ordered list of formats.
/// </summary>
/// <remarks>
/// <para>
/// WHY <c>TryParseExact</c> AND NOT <c>TryParse</c>. A culture's <c>TryParse</c> accepts far more
/// than that culture's own short pattern, so cascading cultures through <c>TryParse</c> means the
/// first culture in the list swallows nearly everything and the rest are decoration. That is
/// precisely the defect this type replaces. Matching against named patterns makes the order
/// meaningful and the behaviour falsifiable.
/// </para>
/// <para>
/// WHY ISO IS ALWAYS FIRST. <c>yyyy-MM-dd</c> cannot be read two ways, so trying it first can never
/// cost a correct answer and always saves an unnecessary ambiguity prompt.
/// </para>
/// <para>
/// Only BCL types are used, so this stays in <c>Pine.Core</c> and is testable without an OCR engine.
/// </para>
/// </remarks>
public static class ReceiptDateParser
{
    /// <summary>Patterns that have exactly one reading.</summary>
    private static readonly string[] UnambiguousFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd",
    ];

    private static readonly string[] DayFirstFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy",
        "dd/MM/yy", "d/M/yy", "dd-MM-yy", "d-M-yy", "dd.MM.yy", "d.M.yy",
    ];

    private static readonly string[] MonthFirstFormats =
    [
        "MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy", "M-d-yyyy", "MM.dd.yyyy", "M.d.yyyy",
        "MM/dd/yy", "M/d/yy", "MM-dd-yy", "M-d-yy", "MM.dd.yy", "M.d.yy",
    ];

    /// <summary>
    /// Matches a date-shaped run of digits. Deliberately permissive about component width and
    /// separator; deciding what the components MEAN is the format list's job, not the regex's.
    /// </summary>
    private static readonly Regex DatePattern = new(
        @"\b(\d{4}[/.\-]\d{1,2}[/.\-]\d{1,2}|\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reports whether the culture writes the day before the month in its short date pattern.
    /// </summary>
    /// <remarks>
    /// The pattern is inspected rather than the culture being compared against a list of names: a
    /// list would have to be maintained, and would be wrong for the first locale nobody thought of.
    /// </remarks>
    public static bool PrefersDayFirst(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var pattern = culture.DateTimeFormat.ShortDatePattern;
        var dayIndex = pattern.IndexOf('d', StringComparison.Ordinal);
        var monthIndex = pattern.IndexOf('M', StringComparison.Ordinal);

        if (dayIndex < 0 || monthIndex < 0)
        {
            return false;
        }

        return dayIndex < monthIndex;
    }

    /// <summary>
    /// Parses the first date-shaped token in <paramref name="text"/>.
    /// </summary>
    /// <param name="text">A line of receipt text, which may contain other words.</param>
    /// <param name="culture">
    /// The culture whose component order is preferred for an ambiguous string. Defaults to
    /// <see cref="CultureInfo.CurrentCulture"/> — the single place this type reads ambient state.
    /// </param>
    /// <param name="now">The clock the plausibility window is measured against. Defaults to now.</param>
    /// <returns>
    /// The preferred reading, flagged when the other reading is equally plausible, or <c>null</c>
    /// when the text holds no plausible date at all.
    /// </returns>
    public static ReceiptDateCandidate? Parse(string text, CultureInfo? culture = null, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = DatePattern.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var token = match.Groups[1].Value;
        var reference = now ?? DateTimeOffset.Now;

        // ISO first: it has one reading, so it can never be the ambiguous case.
        if (TryExact(token, UnambiguousFormats, reference, out var iso))
        {
            return new ReceiptDateCandidate(iso, IsAmbiguous: false, Alternative: null);
        }

        var dayFirstPreferred = PrefersDayFirst(culture ?? CultureInfo.CurrentCulture);

        var preferredFormats = dayFirstPreferred ? DayFirstFormats : MonthFirstFormats;
        var otherFormats = dayFirstPreferred ? MonthFirstFormats : DayFirstFormats;

        var hasPreferred = TryExact(token, preferredFormats, reference, out var preferred);
        var hasOther = TryExact(token, otherFormats, reference, out var other);

        if (!hasPreferred && !hasOther)
        {
            return null;
        }

        if (!hasPreferred)
        {
            // Only one reading survives -- 13/07 cannot be month-first -- so there is nothing to ask.
            return new ReceiptDateCandidate(other, IsAmbiguous: false, Alternative: null);
        }

        if (!hasOther || other == preferred)
        {
            return new ReceiptDateCandidate(preferred, IsAmbiguous: false, Alternative: null);
        }

        return new ReceiptDateCandidate(preferred, IsAmbiguous: true, Alternative: other);
    }

    /// <summary>
    /// Tries every format in order, keeping only a result inside the plausibility window.
    /// </summary>
    /// <remarks>
    /// The window is applied per candidate rather than to whichever string parsed first: applying it
    /// afterwards, as the previous code did, let an implausible reading suppress a plausible one.
    /// <see cref="DateTimeStyles.AssumeLocal"/> is kept so the offset attached matches what the rest
    /// of the pipeline already produces.
    /// </remarks>
    private static bool TryExact(string token, string[] formats, DateTimeOffset now, out DateTimeOffset result)
    {
        foreach (var format in formats)
        {
            if (DateTimeOffset.TryParseExact(token, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
                && parsed.Year > 2000
                && parsed.Year <= now.Year + 1)
            {
                result = parsed;
                return true;
            }
        }

        result = default;
        return false;
    }
}
```

- [ ] **Step 4: Run the tests and verify they pass**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj --filter FullyQualifiedName~ReceiptDateParserTests`
Expected: PASS, 13 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Pine.Core/Ai/ReceiptDateParser.cs tests/Pine.Tests.Unit/Ai/ReceiptDateParserTests.cs
git commit -m "feat(ocr): locale-ordered receipt date parsing with an ambiguity flag (TD-21)"
```

---

### Task 2: Per-field confidence model

**Files:**
- Create: `src/Pine.Core/Ai/ReceiptFieldConfidence.cs`
- Test: `tests/Pine.Tests.Unit/Ai/ReceiptFieldConfidenceTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:

```csharp
namespace Pine.Core.Ai;

public enum ConfidenceBand { NotFound, Low, Medium, High }

public readonly record struct FieldConfidence(double Score, ConfidenceBand Band)
{
    public static FieldConfidence NotFound { get; }
    public static FieldConfidence FromWordScores(IReadOnlyList<float> scores);
    public static ConfidenceBand BandFor(double score);
}

public sealed record ReceiptConfidence(
    FieldConfidence Merchant,
    FieldConfidence Total,
    FieldConfidence Date,
    IReadOnlyList<FieldConfidence> LineItems)
{
    public static ReceiptConfidence None { get; }
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using FluentAssertions;
using Pine.Core.Ai;
using Xunit;

namespace Pine.Tests.Unit.Ai;

public class ReceiptFieldConfidenceTests
{
    [Theory]
    [InlineData(0.95, ConfidenceBand.High)]
    [InlineData(0.90, ConfidenceBand.High)]
    [InlineData(0.899, ConfidenceBand.Medium)]
    [InlineData(0.70, ConfidenceBand.Medium)]
    [InlineData(0.699, ConfidenceBand.Low)]
    [InlineData(0.0, ConfidenceBand.Low)]
    public void The_band_is_a_fixed_function_of_the_score(double score, ConfidenceBand expected)
    {
        FieldConfidence.BandFor(score).Should().Be(expected);
    }

    [Fact]
    public void A_field_is_worth_its_worst_read_word()
    {
        var confidence = FieldConfidence.FromWordScores([0.99f, 0.62f, 0.97f]);

        confidence.Score.Should().BeApproximately(0.62, 0.0001);
        confidence.Band.Should().Be(ConfidenceBand.Low);
    }

    [Fact]
    public void A_field_no_word_produced_is_NotFound_and_not_a_zero_score()
    {
        var confidence = FieldConfidence.FromWordScores([]);

        confidence.Band.Should().Be(ConfidenceBand.NotFound);
        confidence.Should().Be(FieldConfidence.NotFound);
    }

    [Fact]
    public void NotFound_is_distinguishable_from_a_badly_read_field()
    {
        FieldConfidence.NotFound.Band.Should().NotBe(FieldConfidence.FromWordScores([0.01f]).Band);
    }

    [Fact]
    public void An_empty_extraction_reports_every_field_NotFound()
    {
        ReceiptConfidence.None.Merchant.Band.Should().Be(ConfidenceBand.NotFound);
        ReceiptConfidence.None.Total.Band.Should().Be(ConfidenceBand.NotFound);
        ReceiptConfidence.None.Date.Band.Should().Be(ConfidenceBand.NotFound);
        ReceiptConfidence.None.LineItems.Should().BeEmpty();
    }

    [Fact]
    public void A_score_outside_zero_to_one_is_rejected()
    {
        var act = () => FieldConfidence.FromWordScores([1.4f]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj --filter FullyQualifiedName~ReceiptFieldConfidenceTests`
Expected: FAIL — the types do not exist.

- [ ] **Step 3: Implement the model**

```csharp
using System;
using System.Collections.Generic;

namespace Pine.Core.Ai;

/// <summary>How much of a field's reading the OCR engine actually stands behind.</summary>
/// <remarks>
/// <see cref="NotFound"/> is a separate band rather than a score of zero on purpose (TD-22). The
/// previous tally could not tell a merchant that was misread from one that was never on the page:
/// both simply failed to earn their 0.2. Those two need different words on screen — one asks the
/// user to check a value, the other asks them to supply one.
/// </remarks>
public enum ConfidenceBand
{
    NotFound = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>The confidence attached to one extracted field.</summary>
/// <remarks>
/// <para>
/// WHY THE MINIMUM AND NOT THE MEAN. A field is worth what its worst-read word is worth. Averaging
/// lets four crisp words carry one misread digit, and a misread digit in a total is the whole
/// failure this exists to surface.
/// </para>
/// <para>
/// WHY THIS REPLACED A TALLY (TD-22). The score it supersedes was
/// <c>0.1 + 0.2 + 0.25 + 0.2 + 0.15 + 0.1</c> — it counted which fields were populated, never
/// consulted the OCR engine's own per-word confidences, and therefore rated a fully-populated,
/// entirely wrong extraction at roughly 0.9.
/// </para>
/// </remarks>
public readonly record struct FieldConfidence(double Score, ConfidenceBand Band)
{
    /// <summary>The band floor at or above which a reading is reported as high confidence.</summary>
    public const double HighThreshold = 0.90;

    /// <summary>The band floor at or above which a reading is reported as medium confidence.</summary>
    public const double MediumThreshold = 0.70;

    /// <summary>A field that no OCR word produced.</summary>
    public static FieldConfidence NotFound { get; } = new(0.0, ConfidenceBand.NotFound);

    /// <summary>Maps a raw score to its band.</summary>
    public static ConfidenceBand BandFor(double score) => score switch
    {
        >= HighThreshold => ConfidenceBand.High,
        >= MediumThreshold => ConfidenceBand.Medium,
        _ => ConfidenceBand.Low,
    };

    /// <summary>
    /// Builds the confidence of a field from the confidences of the words that produced it.
    /// </summary>
    /// <param name="scores">The per-word confidences, as the OCR engine reported them.</param>
    /// <returns><see cref="NotFound"/> when the list is empty, otherwise the minimum and its band.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A score lies outside 0..1.</exception>
    public static FieldConfidence FromWordScores(IReadOnlyList<float> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);

        if (scores.Count == 0)
        {
            return NotFound;
        }

        var lowest = double.MaxValue;
        foreach (var score in scores)
        {
            if (score < 0f || score > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scores), score, "An OCR word confidence must lie between 0 and 1.");
            }

            if (score < lowest)
            {
                lowest = score;
            }
        }

        return new FieldConfidence(lowest, BandFor(lowest));
    }
}

/// <summary>The per-field confidences of one receipt extraction.</summary>
/// <remarks>
/// There is deliberately no aggregate score. A single percentage cannot tell the user which field to
/// check, which is the only thing they can act on.
/// </remarks>
public sealed record ReceiptConfidence(
    FieldConfidence Merchant,
    FieldConfidence Total,
    FieldConfidence Date,
    IReadOnlyList<FieldConfidence> LineItems)
{
    /// <summary>The confidence of an extraction that read nothing.</summary>
    public static ReceiptConfidence None { get; } = new(
        FieldConfidence.NotFound,
        FieldConfidence.NotFound,
        FieldConfidence.NotFound,
        Array.Empty<FieldConfidence>());
}
```

- [ ] **Step 4: Run the tests and verify they pass**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj --filter FullyQualifiedName~ReceiptFieldConfidenceTests`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Pine.Core/Ai/ReceiptFieldConfidence.cs tests/Pine.Tests.Unit/Ai/ReceiptFieldConfidenceTests.cs
git commit -m "feat(ocr): per-field confidence bands built from real OCR word scores (TD-22)"
```

---

### Task 3: Wire both into `OnnxNlpFieldExtractor` and `ReceiptExtractionResult`

**Files:**
- Modify: `src/Pine.Core/Ai/IAIProvider.cs:30-37` (the `ReceiptExtractionResult` record)
- Modify: `src/Pine.Infrastructure/Ai/Extraction/OnnxNlpFieldExtractor.cs` (lines 66-68, 166-212, 540-590, 825-846)
- Modify: `tests/Pine.Tests.Unit/Application/Transactions/ScanReceiptCommandTests.cs:38,70,119,169`
- Modify: `tests/Pine.Tests.Unit/Infrastructure/ReceiptAdjustmentExtractionTests.cs`
- Test: `tests/Pine.Tests.Unit/Infrastructure/ReceiptExtractionConfidenceTests.cs` (create)

**Interfaces:**
- Consumes: `ReceiptDateParser.Parse`, `ReceiptDateCandidate`, `FieldConfidence.FromWordScores`, `ReceiptConfidence` (Tasks 1 and 2).
- Produces:

```csharp
public record ReceiptExtractionResult(
    string? Merchant,
    decimal? TotalAmount,
    string? CurrencyCode,
    System.DateTimeOffset? Date,
    ReceiptConfidence Confidence,
    System.Collections.Generic.IReadOnlyList<ReceiptLineItem>? LineItems = null,
    bool DateIsAmbiguous = false,
    System.DateTimeOffset? AlternativeDate = null);
```

`double ConfidenceScore` is **removed** from this record. `CategorisationResult.ConfidenceScore` (`IAIProvider.cs:44`) is a different type and stays exactly as it is — do not touch it, and do not touch `CategorizeTransactionQuery.cs:31` or `ImportTransactionsCommand.cs:256`, which read that one.

- [ ] **Step 1: Write the failing tests**

Create `tests/Pine.Tests.Unit/Infrastructure/ReceiptExtractionConfidenceTests.cs`. Read `ReceiptAdjustmentExtractionTests.cs` first and reuse its private `OcrWord` helpers rather than writing new ones.

```csharp
using System;
using System.Collections.Generic;
using FluentAssertions;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Extraction;
using Xunit;

namespace Pine.Tests.Unit.Infrastructure;

public class ReceiptExtractionConfidenceTests
{
    private static OcrWord Word(string text, float x, float y, float confidence) => new()
    {
        Text = text,
        Confidence = confidence,
        BoundingBox = new BoundingBox { X = x, Y = y, Width = 10 * text.Length, Height = 10 },
    };

    private static ReceiptExtractionResult Extract(params OcrWord[] words)
    {
        using var extractor = new OnnxNlpFieldExtractor();
        return extractor.ExtractFields(words);
    }

    [Fact]
    public void A_badly_read_total_reports_a_low_band()
    {
        var result = Extract(
            Word("ACME", 0, 0, 0.99f),
            Word("TOTAL", 0, 100, 0.98f),
            Word("24.50", 100, 100, 0.41f));

        result.Confidence.Total.Band.Should().Be(ConfidenceBand.Low);
        result.Confidence.Total.Score.Should().BeApproximately(0.41, 0.01);
    }

    [Fact]
    public void A_merchant_that_was_never_on_the_page_is_NotFound()
    {
        var result = Extract(
            Word("TOTAL", 0, 100, 0.98f),
            Word("24.50", 100, 100, 0.97f));

        result.Confidence.Merchant.Band.Should().Be(ConfidenceBand.NotFound);
    }

    [Fact]
    public void An_empty_word_list_produces_no_confidence_and_no_invented_date()
    {
        var result = Extract();

        result.Confidence.Should().Be(ReceiptConfidence.None);
        result.Date.Should().BeNull();
    }

    [Fact]
    public void An_ambiguous_receipt_date_is_flagged_with_its_alternative()
    {
        var result = Extract(
            Word("FECHA:", 0, 50, 0.99f),
            Word("05/03/2026", 100, 50, 0.99f),
            Word("TOTAL", 0, 100, 0.98f),
            Word("24.50", 100, 100, 0.97f));

        result.DateIsAmbiguous.Should().BeTrue();
        result.AlternativeDate.Should().NotBeNull();
        result.AlternativeDate!.Value.Date.Should().NotBe(result.Date!.Value.Date);
    }
}
```

- [ ] **Step 2: Run and verify it fails**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj --filter FullyQualifiedName~ReceiptExtractionConfidenceTests`
Expected: FAIL — `ReceiptExtractionResult` has no `Confidence` member.

- [ ] **Step 3: Change the record and the extractor**

1. In `src/Pine.Core/Ai/IAIProvider.cs`, replace the `ReceiptExtractionResult` record with the shape in the **Interfaces** block above, carrying a doc comment that states why the aggregate score is gone (TD-22) and why the date carries an alternative (TD-21).

2. In `OnnxNlpFieldExtractor.cs`:
   - Delete `ComputeConfidence` entirely (lines 825-846) and its call site at line 187.
   - Delete `ParseDateFromText` (lines 566-590) and the `DateRegex` generated regex (lines 66-68). `ExtractDate` now calls `ReceiptDateParser.Parse(line.Text)` and returns `ReceiptDateCandidate?`. Its two strategies (keyword-anchored first, then any line) are unchanged.
   - The `DateRegex().IsMatch(line.Text)` guard at line 347 becomes `ReceiptDateParser.Parse(line.Text) is not null`.
   - `ExtractMerchant`, `CrossValidateTotal` and `ExtractLineItems` each additionally yield the `OcrWord`s they consumed, so their confidences can be read. Do this by having each return, or expose via an `out`, the `List<OcrWord>` from the `ClusteredLine.Words` it selected; `ClusteredLine` already carries that list (`OnnxNlpFieldExtractor.cs:849`). Do not re-run any extraction logic to find the words a second time — a second pass could disagree with the first and the confidence would then describe a field that was not the one returned.
   - Build the result:

```csharp
var confidence = new ReceiptConfidence(
    Merchant: FieldConfidence.FromWordScores(Scores(merchantWords)),
    Total: FieldConfidence.FromWordScores(Scores(totalWords)),
    Date: FieldConfidence.FromWordScores(Scores(dateWords)),
    LineItems: lineItemWords.Select(words => FieldConfidence.FromWordScores(Scores(words))).ToList());

return new ReceiptExtractionResult(
    Merchant: merchant,
    TotalAmount: totalAmount,
    CurrencyCode: currency,
    Date: dateCandidate?.Value,
    Confidence: confidence,
    LineItems: lineItems,
    DateIsAmbiguous: dateCandidate?.IsAmbiguous ?? false,
    AlternativeDate: dateCandidate?.Alternative);
```

with

```csharp
private static IReadOnlyList<float> Scores(IReadOnlyList<OcrWord> words)
{
    var scores = new float[words.Count];
    for (var i = 0; i < words.Count; i++)
    {
        scores[i] = Math.Clamp(words[i].Confidence, 0f, 1f);
    }

    return scores;
}
```

   An empty word list therefore yields `FieldConfidence.NotFound` through `FromWordScores`, which is the contract Task 2 fixed. `Math.Clamp` is there because the engine's reported confidence is not contractually bounded and `FromWordScores` throws outside 0..1; clamping at the boundary keeps a rogue engine value from crashing a scan.

   - The empty-input early return at line 169 becomes
     `new ReceiptExtractionResult("Unknown", 0m, "USD", null, ReceiptConfidence.None)`. Note the date is now `null`, not `DateTimeOffset.UtcNow`: inventing today's date for an image with no words is the same class of unearned claim this ticket removes.

3. Update the four `new ReceiptExtractionResult(...)` calls in `ScanReceiptCommandTests.cs` (lines 38, 70, 119, 169) to pass `ReceiptConfidence.None` where they passed `0.9` / `0.5`. Update `ReceiptAdjustmentExtractionTests.cs` if its helper or assertions reference `ConfidenceScore`.

- [ ] **Step 4: Run the full unit suite**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj`
Expected: PASS. If `Pine.Ui.Avalonia` fails to build because of `ConfidenceScore`, that is Task 4's work — comment nothing out; complete Task 4 before committing if the solution will not build.

- [ ] **Step 5: Commit**

```bash
git add src/Pine.Core/Ai/IAIProvider.cs src/Pine.Infrastructure/Ai/Extraction/OnnxNlpFieldExtractor.cs tests/
git commit -m "feat(ocr): real per-field confidence and flagged date ambiguity in the extractor (TD-21, TD-22)"
```

---

### Task 4: Surface ambiguity and weak fields in the confirmation screen

**Files:**
- Modify: `src/Pine.Ui.Avalonia/ViewModels/ScanReceiptViewModel.cs:76, 180, 205`
- Modify: `src/Pine.Ui.Avalonia/Views/ScanReceiptView.axaml:196-202`
- Modify: `src/Pine.Ui.Avalonia/Utils/Converters.cs`
- Test: `tests/Pine.Tests.Unit/ViewModels/ScanReceiptConfidenceTests.cs` (create)

**Interfaces:**
- Consumes: `ReceiptExtractionResult.Confidence`, `.DateIsAmbiguous`, `.AlternativeDate` (Task 3).
- Produces on `ScanReceiptViewModel`:

```csharp
public partial ConfidenceBand MerchantConfidence { get; private set; }
public partial ConfidenceBand TotalConfidence { get; private set; }
public partial ConfidenceBand DateConfidence { get; private set; }
public partial bool IsDateAmbiguous { get; private set; }
public partial DateTimeOffset? AlternativeDate { get; private set; }
public string? DateAmbiguityMessage { get; }   // null when not ambiguous
[RelayCommand] private void UseAlternativeDate();
```

- [ ] **Step 1: Write the failing tests**

Read `tests/Pine.Tests.Unit/ViewModels/` first and copy the construction pattern the existing `ScanReceipt*` view-model tests use (NSubstitute `IMediator`, `INavigationService`). Write these four:

```csharp
[Fact]
public async Task An_ambiguous_date_offers_the_other_reading()
{
    // mediator returns a ReceiptExtractionResult with DateIsAmbiguous: true,
    // Date: 2026-03-05, AlternativeDate: 2026-05-03
    await vm.ProcessReceiptImageAsync(new MemoryStream(Array.Empty<byte>()));

    vm.IsDateAmbiguous.Should().BeTrue();
    vm.DateAmbiguityMessage.Should().Contain("May");
    vm.Date.Month.Should().Be(3);
}

[Fact]
public async Task Taking_the_alternative_swaps_the_two_readings()
{
    await vm.ProcessReceiptImageAsync(new MemoryStream(Array.Empty<byte>()));

    vm.UseAlternativeDateCommand.Execute(null);

    vm.Date.Month.Should().Be(5);
    vm.AlternativeDate!.Value.Month.Should().Be(3);   // the swap is reversible
    vm.IsDateAmbiguous.Should().BeTrue();             // the question stays answered-but-open
}

[Fact]
public async Task A_weakly_read_total_is_reported_as_such()
{
    // extraction result whose Confidence.Total is FieldConfidence.FromWordScores([0.41f])
    await vm.ProcessReceiptImageAsync(new MemoryStream(Array.Empty<byte>()));

    vm.TotalConfidence.Should().Be(ConfidenceBand.Low);
}

[Fact]
public async Task A_second_scan_clears_the_previous_bands()
{
    // first scan: Total 0.41 (Low); second scan: Total 0.99 (High)
    await vm.ProcessReceiptImageAsync(new MemoryStream(Array.Empty<byte>()));
    await vm.ProcessReceiptImageAsync(new MemoryStream(Array.Empty<byte>()));

    vm.TotalConfidence.Should().Be(ConfidenceBand.High);
    vm.IsDateAmbiguous.Should().BeFalse();
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj --filter FullyQualifiedName~ScanReceiptConfidenceTests`
Expected: FAIL — the members do not exist.

- [ ] **Step 3: Implement**

- Delete `public partial double? ConfidenceScore` (line 76) and both assignments (lines 180, 205).
- Add the members above; assign them from `result.Confidence` in `ProcessReceiptImageAsync`, and reset all of them at the top of that method alongside the existing `ErrorMessage` reset — a second scan must not inherit the first scan's bands.
- `DateAmbiguityMessage` reads, when ambiguous: `$"This could also be {AlternativeDate:d MMMM yyyy}. Check the receipt."` and is `null` otherwise. Notify it from `OnIsDateAmbiguousChanged` and `OnAlternativeDateChanged` so the binding updates.
- `UseAlternativeDate` swaps `Date` and `AlternativeDate` and leaves `IsDateAmbiguous` true, so the user can swap back.
- In `ScanReceiptView.axaml`, replace the percentage `TextBlock` (lines 196-202) with: a warning row bound to `IsDateAmbiguous` showing `DateAmbiguityMessage` and a button bound to `UseAlternativeDateCommand`, plus a low-confidence hint next to the merchant, total and date fields, each visible when its band is `Low` or `NotFound`. Add the band-to-visibility converter to `src/Pine.Ui.Avalonia/Utils/Converters.cs` following the converters already there — do not create a second converter file.

- [ ] **Step 4: Run the unit suite and build the UI heads**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj`
Run: `dotnet build src/Pine.Ui.Avalonia/Pine.Ui.Avalonia.csproj src/Pine.Ui.Windows/Pine.Ui.Windows.csproj`
Expected: PASS and 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Pine.Ui.Avalonia tests/Pine.Tests.Unit/ViewModels/ScanReceiptConfidenceTests.cs
git commit -m "feat(ui): show weak OCR fields and ask about an ambiguous date (TD-21, TD-22)"
```

---

### Task 5: `ImportKeyFactory.FromReceipt` — the `s1:` scheme

**Files:**
- Modify: `src/Pine.Core/Imports/ImportKeyFactory.cs`
- Test: `tests/Pine.Tests.Unit/Domain/ImportKeyFactoryReceiptTests.cs` (create; if an `ImportKeyFactory` test file already exists, extend that instead)

**Interfaces:**
- Consumes: nothing.
- Produces:

```csharp
public const string ReceiptSchemePrefix = "s1:";

public static string FromReceipt(
    Guid accountId,
    DateTimeOffset date,
    decimal amount,
    string currencyCode,
    string merchant);
```

**The amount is the SIGNED transaction amount**, i.e. negative for a receipt, because that is what `ScanReceiptViewModel` sends to `AddTransactionCommand` (`ScanReceiptViewModel.cs:305`, `decimal finalAmount = -Amount;`). Deriving the key from the positive printed figure in one place and the negative stored figure in another is the single most likely way for this feature to look correct and never match anything.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using FluentAssertions;
using Pine.Core.Importing;
using Xunit;

namespace Pine.Tests.Unit.Domain;

public class ImportKeyFactoryReceiptTests
{
    private static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Date = new(2026, 3, 5, 14, 30, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void The_same_receipt_derives_the_same_key()
    {
        var first = ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "ACME Coffee");
        var second = ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "  acme   coffee ");

        second.Should().Be(first);
    }

    [Fact]
    public void The_key_carries_the_receipt_scheme_prefix()
    {
        ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "ACME")
            .Should().StartWith("s1:");
    }

    [Fact]
    public void A_receipt_key_never_collides_with_an_import_key()
    {
        var receipt = ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "ACME");
        var derived = ImportKeyFactory.Derive(Date, -24.50m, "USD", "ACME", "statement.csv", 0);

        receipt.Should().NotBe(derived);
        receipt.Should().NotStartWith(ImportKeyFactory.DerivedSchemePrefix);
    }

    [Fact]
    public void The_time_of_day_does_not_change_the_key()
    {
        var morning = ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "ACME");
        var evening = ImportKeyFactory.FromReceipt(Account, Date.AddHours(6), -24.50m, "USD", "ACME");

        evening.Should().Be(morning);
    }

    [Theory]
    [InlineData("22222222-2222-2222-2222-222222222222", -24.50, "USD", "ACME")]
    [InlineData("11111111-1111-1111-1111-111111111111", -24.51, "USD", "ACME")]
    [InlineData("11111111-1111-1111-1111-111111111111", -24.50, "EUR", "ACME")]
    [InlineData("11111111-1111-1111-1111-111111111111", -24.50, "USD", "BETA")]
    public void Every_field_participates_in_the_key(string account, decimal amount, string currency, string merchant)
    {
        var baseline = ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "ACME");

        ImportKeyFactory.FromReceipt(Guid.Parse(account), Date, amount, currency, merchant)
            .Should().NotBe(baseline);
    }

    [Fact]
    public void The_key_fits_the_persisted_column()
    {
        ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", new string('x', 500))
            .Length.Should().BeLessThanOrEqualTo(ImportKeyFactory.MaxLength);
    }

    [Fact]
    public void A_missing_currency_is_refused()
    {
        var act = () => ImportKeyFactory.FromReceipt(Account, Date, -24.50m, " ", "ACME");

        act.Should().Throw<ArgumentException>();
    }
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj --filter FullyQualifiedName~ImportKeyFactoryReceiptTests`
Expected: FAIL — `FromReceipt` does not exist.

- [ ] **Step 3: Implement**

Add to `ImportKeyFactory`, reusing its existing private `Normalise`, `Hash` and `FieldSeparator` members:

```csharp
/// <summary>The scheme prefix for keys derived from a scanned receipt.</summary>
public const string ReceiptSchemePrefix = "s1:";

/// <summary>
/// Derives the key of a scanned receipt, so a second scan of the same purchase can be recognised
/// (TD-20).
/// </summary>
/// <remarks>
/// <para>
/// WHY A SEPARATE SCHEME. A receipt has no external reference and no row index, so neither
/// <see cref="FromReference"/> nor <see cref="Derive"/> describes it. Giving it its own prefix
/// keeps the two namespaces from ever answering for each other, which is the rule this type's
/// VERSION PREFIXES remark already sets out.
/// </para>
/// <para>
/// WHY THE ACCOUNT IS IN THE KEY. The same purchase posted to two accounts is two transactions.
/// The lookup is already scoped by account, so this is belt and braces — but it makes the key
/// self-describing rather than only meaningful next to the query that used it.
/// </para>
/// <para>
/// THE AMOUNT IS THE SIGNED LEDGER AMOUNT, not the printed one. The caller converts a receipt to
/// an expense before it ever reaches a command; deriving the key from the printed positive figure
/// would produce a key that never matches anything the vault stores.
/// </para>
/// <para>
/// Only the calendar date participates: the same purchase re-scanned carries a re-read timestamp,
/// and a receipt's meaningful granularity is the day.
/// </para>
/// </remarks>
public static string FromReceipt(
    Guid accountId,
    DateTimeOffset date,
    decimal amount,
    string currencyCode,
    string merchant)
{
    if (string.IsNullOrWhiteSpace(currencyCode))
        throw new ArgumentException("Currency code is required.", nameof(currencyCode));

    var canonical = new StringBuilder()
        .Append(accountId.ToString("D", CultureInfo.InvariantCulture))
        .Append(FieldSeparator)
        .Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        .Append(FieldSeparator)
        .Append(amount.ToString(CultureInfo.InvariantCulture))
        .Append(FieldSeparator)
        .Append(Normalise(currencyCode))
        .Append(FieldSeparator)
        .Append(Normalise(merchant))
        .ToString();

    return ReceiptSchemePrefix + Hash(canonical);
}
```

- [ ] **Step 4: Run and verify the tests pass**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj --filter FullyQualifiedName~ImportKeyFactoryReceiptTests`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Pine.Core/Imports/ImportKeyFactory.cs tests/Pine.Tests.Unit/Domain/ImportKeyFactoryReceiptTests.cs
git commit -m "feat(imports): an s1 receipt key scheme for duplicate detection (TD-20)"
```

---

### Task 6: The duplicate lookup, the command's key, and the warning

**Files:**
- Modify: `src/Pine.Core/Transactions/ITransactionRepository.cs`
- Modify: `src/Pine.Infrastructure/Data/SqliteTransactionRepository.cs`
- Create: `src/Pine.Application/Transactions/Queries/CheckReceiptDuplicateQuery.cs`
- Modify: `src/Pine.Application/Transactions/Commands/AddTransactionCommand.cs` (the record and the handler)
- Modify: `src/Pine.Ui.Avalonia/ViewModels/ScanReceiptViewModel.cs` (`SaveTransactionAsync`, from line 266)
- Modify: `src/Pine.Ui.Avalonia/Views/ScanReceiptView.axaml`
- Modify: `tests/Pine.Tests.Integration/Atomicity/AtomicityHost.cs` (its fake repository must implement the new member)
- Test: `tests/Pine.Tests.Unit/Application/Transactions/CheckReceiptDuplicateQueryTests.cs` (create)
- Test: `tests/Pine.Tests.Unit/ViewModels/ScanReceiptDuplicateTests.cs` (create)
- Test: `tests/Pine.Tests.Integration/Repositories/ReceiptDuplicateLookupTests.cs` (create)

**Interfaces:**
- Consumes: `ImportKeyFactory.FromReceipt` (Task 5), `GetExistingImportKeysAsync` (already present).
- Produces:

```csharp
// ITransactionRepository
Task<Transaction?> GetByImportKeyAsync(Guid accountId, string importKey, CancellationToken cancellationToken = default);

// Pine.Application.Transactions.Queries
public sealed record ReceiptDuplicateMatch(Guid TransactionId, DateTimeOffset Date, decimal Amount, string Payee);

public sealed record CheckReceiptDuplicateQuery(
    Guid AccountId,
    DateTimeOffset Date,
    decimal SignedAmount,
    string CurrencyCode,
    string Merchant) : IRequest<ReceiptDuplicateMatch?>;

// AddTransactionCommand gains a final optional parameter
string? ImportKey = null
```

- [ ] **Step 1: Write the failing tests**

Unit — the query:

```csharp
[Fact]
public async Task A_receipt_matching_a_stored_key_reports_the_transaction()
{
    // repository substitute: GetByImportKeyAsync returns a transaction dated 2026-03-05, -24.50, "ACME"
    var match = await handler.Handle(
        new CheckReceiptDuplicateQuery(account, date, -24.50m, "USD", "ACME"), default);

    match.Should().NotBeNull();
    match!.Payee.Should().Be("ACME");
    match.Amount.Should().Be(-24.50m);
}

[Fact]
public async Task A_receipt_with_no_match_reports_nothing()
{
    // repository substitute returns null
    (await handler.Handle(query, default)).Should().BeNull();
}

[Fact]
public async Task The_query_asks_for_the_key_the_factory_derives()
{
    await handler.Handle(new CheckReceiptDuplicateQuery(account, date, -24.50m, "USD", "ACME"), default);

    await repository.Received(1).GetByImportKeyAsync(
        account,
        ImportKeyFactory.FromReceipt(account, date, -24.50m, "USD", "ACME"),
        Arg.Any<CancellationToken>());
}
```

Unit — the view model:

```csharp
[Fact]
public async Task Saving_a_duplicate_warns_instead_of_writing()
{
    // mediator: CheckReceiptDuplicateQuery -> a match
    await vm.SaveTransactionCommand.ExecuteAsync(null);

    vm.DuplicateWarning.Should().Contain("24.50");
    await mediator.DidNotReceive().Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task Saving_again_after_the_warning_writes_the_transaction()
{
    await vm.SaveTransactionCommand.ExecuteAsync(null);
    await vm.SaveTransactionCommand.ExecuteAsync(null);

    await mediator.Received(1).Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task Editing_the_date_after_a_warning_re_checks_before_writing()
{
    await vm.SaveTransactionCommand.ExecuteAsync(null);   // warned
    vm.Date = vm.Date.AddDays(-1);                        // the user corrects it
    await vm.SaveTransactionCommand.ExecuteAsync(null);

    // the acknowledgement does not survive an edit: it warned about a different transaction
    await mediator.Received(2).Send(Arg.Any<CheckReceiptDuplicateQuery>(), Arg.Any<CancellationToken>());
    await mediator.DidNotReceive().Send(Arg.Any<AddTransactionCommand>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task The_saved_command_carries_the_receipt_key()
{
    await vm.SaveTransactionCommand.ExecuteAsync(null);   // no duplicate

    await mediator.Received(1).Send(
        Arg.Is<AddTransactionCommand>(c => c.ImportKey!.StartsWith("s1:")),
        Arg.Any<CancellationToken>());
}
```

Integration — the repository, against a real vault, following the pattern in `tests/Pine.Tests.Integration/Repositories/`:

```csharp
[Fact]
public async Task A_transaction_saved_with_a_receipt_key_is_found_by_it()
{
    // save via AddTransactionCommand with ImportKey, then look it up
    var found = await repository.GetByImportKeyAsync(accountId, key);

    found.Should().NotBeNull();
    found!.ImportKey.Should().Be(key);
}

[Fact]
public async Task The_lookup_is_scoped_to_the_account()
{
    (await repository.GetByImportKeyAsync(otherAccountId, key)).Should().BeNull();
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj --filter "FullyQualifiedName~CheckReceiptDuplicate|FullyQualifiedName~ScanReceiptDuplicate"`
Expected: FAIL — the query and the view-model members do not exist.

- [ ] **Step 3: Implement**

1. `ITransactionRepository.GetByImportKeyAsync` with a doc comment saying it serves receipt duplicate detection and that a null key never matches. Implement in `SqliteTransactionRepository` with `FirstOrDefaultAsync(t => t.AccountId == accountId && t.ImportKey == importKey)` — the `(AccountId, ImportKey)` index from migration `20260827141109` already covers it, so no migration is needed. Implement the member on the fake repository in `AtomicityHost.cs` too.
2. `CheckReceiptDuplicateQuery` + handler, which derives the key with `ImportKeyFactory.FromReceipt` and projects the found transaction to `ReceiptDuplicateMatch`. It returns a projection, not the aggregate, so the UI cannot mutate a transaction it only meant to warn about.
3. `AddTransactionCommand` gains `string? ImportKey = null` as its last parameter; the handler sets it on the transaction. Look at how `ImportTransactionsCommand` sets `ImportKey` on a transaction (`ImportTransactionsCommand.cs:176`) and use the same mechanism — do not add a second one.
4. In `ScanReceiptViewModel`: add an observable `DuplicateWarning` string and a private `string? _acknowledgedDuplicateKey`. In `SaveTransactionAsync`, after the existing reconciliation guard and before the write, derive the current key, send `CheckReceiptDuplicateQuery`; if a match comes back and the key differs from `_acknowledgedDuplicateKey`, set `DuplicateWarning` (naming the matched transaction's date and amount), store the key in `_acknowledgedDuplicateKey`, and return without writing. A second press with an unchanged key proceeds. Any edit to `Date`, `Amount`, `Merchant` or `SelectedAccount` clears `_acknowledgedDuplicateKey` — implement that in the `On<Property>Changed` partial methods the CommunityToolkit source generator already provides, as this file does elsewhere.
5. Pass `ImportKeyFactory.FromReceipt(...)` as the command's `ImportKey`, derived from `finalAmount` — the negated value — so the stored key matches what the next scan will compute.
6. In `ScanReceiptView.axaml`, add a warning row bound to `DuplicateWarning`, visible when it is non-empty, styled like the existing `ErrorMessage` row.

- [ ] **Step 4: Run both suites**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj`
Run: `dotnet test tests/Pine.Tests.Integration/Pine.Tests.Integration.csproj`
Expected: both green.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(ocr): warn on a duplicate receipt before it is written (TD-20)"
```

---

### Task 7: Falsification mutations and the audit record

**Files:**
- Modify: `docs/60-Production-Readiness-Audit.md` (§12 remediation log, the Phase 1 checklist at line 631, the TD-20/21/22 register rows at lines 505-507, the header `Remediation` and `Last updated` cells)
- Modify: `docs/61-Manual-Test-Plan.md` (a section for the ambiguity prompt, the weak-field highlighting and the duplicate warning — all three are UI behaviours no test renders)

- [ ] **Step 1: Run each mutation**

For each of the seven below: make the edit, run the named suite, record which test failed, then `git checkout --` the file. A mutation that survives is a finding and goes in the write-up **as a surviving mutation**, not quietly fixed and forgotten.

1. In `ReceiptDateParser.Parse`, invert `dayFirstPreferred`.
2. In `ReceiptDateParser.Parse`, return `IsAmbiguous: false` unconditionally.
3. In `FieldConfidence.FromWordScores`, take the maximum instead of the minimum.
4. In `FieldConfidence.FromWordScores`, return `BandFor(0)` (i.e. `Low`) instead of `NotFound` for an empty list.
5. In `ImportKeyFactory.FromReceipt`, return `DerivedSchemePrefix + Hash(canonical)`.
6. In `ScanReceiptViewModel`, do not clear `_acknowledgedDuplicateKey` when `Date` changes.
7. In `ReceiptDateParser.TryExact`, use `DateTimeOffset.TryParse(token, CultureInfo.InvariantCulture, ...)` instead of `TryParseExact` — this asserts that TD-21's original cause is itself covered.

- [ ] **Step 2: Run both suites clean and record the counts**

Run: `dotnet test tests/Pine.Tests.Unit/Pine.Tests.Unit.csproj`
Run: `dotnet test tests/Pine.Tests.Integration/Pine.Tests.Integration.csproj`
Record the exact totals. The previous iteration ended at 582 unit + 187 integration; report the new figures from this run, never from an estimate.

- [ ] **Step 3: Write §12.38**

Follow the shape of §12.36: what the ticket was, what was found to be true against the code rather than the register, what was built, the mutation table with survivors named, the test counts, what was deliberately left undone, and a "Next" list. Tick the Phase 1 item-7 checkbox at line 631 and move TD-20, TD-21 and TD-22 to ✅ in the register with the section reference. Update the header's `Remediation` cell and `Last updated`.

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "docs: record Phase 1 item 7 in the audit and the manual test plan (TD-20, TD-21, TD-22)"
```
