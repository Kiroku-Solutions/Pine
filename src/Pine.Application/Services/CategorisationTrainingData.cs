using System;
using System.Collections.Generic;
using System.Linq;
using Pine.Core.Ai;
using Pine.Core.Transactions;

namespace Pine.Application.Services;

/// <summary>
/// Turns the ledger into labelled examples for the categoriser (TD-32).
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS IS A NAMED TYPE AND NOT FOUR LINES INSIDE THE BACKGROUND SERVICE. It used to be four
/// lines inside <see cref="CategorizationTrainingBackgroundService"/>, and for as long as it was,
/// **it discarded every example it was given and nobody could see it happen.** The projection read
/// <c>Guid.TryParse(split.Category, out var g) ? g : Guid.Empty</c> and then filtered
/// <c>CategoryId != Guid.Empty</c> — but <c>TransactionSplit.Category</c> holds a category NAME
/// typed by a person ("Groceries") or read from a CSV column. A name never parses as a
/// <see cref="Guid"/>, so the filter removed everything, the training set was always empty,
/// <c>TrainModelAsync</c> was never reached, and the watermark was never advanced.
/// </para>
/// <para>
/// That is TD-32 — "categories modelled twice" — with its real cost attached. The two models were
/// not merely redundant: the ledger side spoke a string and the machine-learning side spoke a
/// <see cref="Guid"/>, and the adapter between them was a parse that could not succeed. The audit
/// row called this a tidiness problem about renames. It was a feature that had never once run.
/// </para>
/// <para>
/// WHICH MODEL WON, AND WHY IT IS NOT THE ONE THE ROW NAMED. TD-32 prescribed storing a
/// <c>CategoryId</c> on the split. That direction requires <c>Category</c> rows to exist, and
/// nothing in this application has ever created one: <c>CreateCategoryCommand</c> is called by
/// tests only, there is no query that lists categories, and every category input in the UI is a
/// free <c>TextBox</c>. Pointing the split at an FK into a permanently empty table would have
/// replaced a broken feature with an unusable one. So the string won — because it is the model
/// that has instances — and the identifier was removed from the learning path entirely.
/// </para>
/// </remarks>
public static class CategorisationTrainingData
{
    /// <summary>
    /// One labelled example per categorised split: the payee is the text, the split's own amount
    /// is the amount, and the split's category is the label.
    /// </summary>
    /// <remarks>
    /// The label is the category exactly as stored, trimmed. Trimming is deliberate and is the only
    /// normalisation applied: " Groceries" and "Groceries" are one category to a person, and a
    /// model that treats them as two learns a distinction that does not exist. Case is NOT folded —
    /// that would silently merge categories a user chose to spell differently, which is a decision
    /// for a category-management feature that does not exist yet, not for a training projection.
    /// </remarks>
    public static IReadOnlyList<TrainingExample> From(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return transactions
            .SelectMany(transaction => transaction.Splits
                .Where(split => !string.IsNullOrWhiteSpace(split.Category))
                .Select(split => new TrainingExample(
                    transaction.Payee,
                    split.Amount.Amount,
                    split.Category.Trim())))
            .ToList();
    }

    /// <summary>
    /// The number of transactions considered "categorised" for the purposes of the training
    /// threshold.
    /// </summary>
    /// <remarks>
    /// Kept beside <see cref="From"/> on purpose. The watermark counts TRANSACTIONS and the
    /// training set counts SPLITS, so the two numbers legitimately differ — a transaction with
    /// three categorised splits is one towards the threshold and three towards the model. When
    /// these two lived apart, that difference read like a bug; here it reads like the distinction
    /// it is.
    /// </remarks>
    public static int CountCategorised(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        return transactions.Count(transaction =>
            transaction.Splits.Any(split => !string.IsNullOrWhiteSpace(split.Category)));
    }
}
