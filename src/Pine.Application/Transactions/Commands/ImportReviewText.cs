using Pine.Core.Common;

namespace Pine.Application.Transactions.Commands;

/// <summary>
/// The reason each imported row shows on the review step, one factory per key (TD-139).
/// </summary>
/// <remarks>
/// <para>
/// THESE WERE BELOW THE UI AND IN ENGLISH, AND TD-139 DID NOT COUNT THEM. The row listed two sets —
/// the call sites' fallbacks and the exception sentences — and missed a third: the import handler
/// wrote a finished English sentence into every <see cref="ImportReviewRow"/>, and the wizard showed it
/// verbatim beside each row. Same defect, different carrier.
/// </para>
/// <para>
/// Same rules as <see cref="UserFacingText"/>: one method per key, its parameters the template's
/// placeholders, checked against every shipped table by <c>UserFacingCatalogTests</c>.
/// </para>
/// </remarks>
public static class ImportReviewText
{
    /// <summary>A row written as a new transaction, after passing the duplicate check.</summary>
    public static UserFacingMessage Imported() => new("ImportReview.Imported");

    /// <summary>A row written with an amount of zero (typically a bank authorization), kept for the user to hide or keep.</summary>
    public static UserFacingMessage ImportedZeroAmount() => new("ImportReview.ImportedZeroAmount");

    /// <summary>A row written without a duplicate check, because its importer supplied no key.</summary>
    public static UserFacingMessage ImportedWithoutKeyCheck() => new("ImportReview.ImportedWithoutKeyCheck");

    /// <summary>A row skipped because the account already holds it from an earlier import.</summary>
    public static UserFacingMessage SkippedAlreadyInAccount(string accountName)
        => new("ImportReview.SkippedAlreadyInAccount", accountName);

    /// <summary>A row skipped because an earlier row in the same file is identical.</summary>
    public static UserFacingMessage SkippedDuplicateInFile() => new("ImportReview.SkippedDuplicateInFile");
}
