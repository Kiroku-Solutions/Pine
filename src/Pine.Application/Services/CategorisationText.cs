using System;

namespace Pine.Application.Services;

/// <summary>
/// The one place the text the categoriser sees is built (TD-91).
/// </summary>
/// <remarks>
/// Training used to fit the model on the payee alone while import categorised on "payee notes", so
/// the model was served inputs shaped unlike those it was fitted on. Training
/// (<see cref="CategorisationTrainingData"/>) and every prediction call site go through
/// <see cref="Build"/>, so the two can no longer drift apart.
/// </remarks>
public static class CategorisationText
{
    /// <summary>
    /// Builds the feature text for one transaction: the payee, a single space, the notes, trimmed.
    /// Either part may be null or empty.
    /// </summary>
    public static string Build(string? payee, string? notes) => $"{payee} {notes}".Trim();
}
