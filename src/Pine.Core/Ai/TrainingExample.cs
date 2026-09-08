namespace Pine.Core.Ai;

/// <summary>
/// One labelled example for the transaction categoriser (TD-32).
/// </summary>
/// <param name="Text">The payee. This is the feature the model reads.</param>
/// <param name="Amount">The split's own amount, not the transaction's total.</param>
/// <param name="Category">
/// The label: the category as it is stored on the split, which is a name a person typed or a CSV
/// column supplied.
/// </param>
/// <remarks>
/// <para>
/// THE LABEL IS A STRING, AND THAT IS THE FIX RATHER THAN THE COMPROMISE. This contract used to be
/// a bare tuple carrying a <see cref="System.Guid"/> <c>CategoryId</c>, while the only thing that
/// has ever produced a label — <c>TransactionSplit.Category</c> — is a name. The adapter between
/// the two was <c>Guid.TryParse</c> on that name, which cannot succeed, so every example was
/// discarded and the model was never trained (TD-32).
/// </para>
/// <para>
/// Making the label a string means the type now matches the only data that exists. The alternative
/// — making the ledger produce a <see cref="System.Guid"/> — needs a populated <c>Categories</c>
/// table, and nothing in this application has ever inserted a row into one.
/// </para>
/// </remarks>
public readonly record struct TrainingExample(string Text, decimal Amount, string Category);
