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
