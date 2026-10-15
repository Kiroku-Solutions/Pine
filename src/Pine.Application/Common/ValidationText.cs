using System;
using Pine.Core.Common;

namespace Pine.Application.Common;

/// <summary>
/// Every <see cref="UserFacingMessage"/> a validator in <c>Pine.Application</c> can report, one
/// factory per key (TD-139).
/// </summary>
/// <remarks>
/// <para>
/// THE SAME RULES AS <see cref="UserFacingText"/>, AND THE SAME TEST. One method per key; its
/// parameters are the template's placeholders, in order; <c>UserFacingCatalogTests</c> holds every
/// method's key present in every shipped table with exactly that many placeholders.
/// </para>
/// <para>
/// NO ENTITY ID IS AN ARGUMENT, AND SOME USED TO BE. The English sentences these replace included
/// "Account 3f2a…-… was not found." and "Transaction 9c1e…-… was not found." — an identifier the user
/// never saw, on screen, which is the TD-24 rule broken by a message that opted in to being shown.
/// The identifier is still in <see cref="ValidationError.Message"/>, which is the log's half, and is
/// no longer in the user's.
/// </para>
/// </remarks>
public static class ValidationText
{
    /// <summary>Any request whose request-level refusal carried no individual reason.</summary>
    public static UserFacingMessage RequestRejected() => new("Validation.RequestRejected");

    /// <summary>An account named by a request that no longer exists.</summary>
    public static UserFacingMessage AccountNotFound() => new("Validation.AccountNotFound");

    /// <summary>A transaction named by a request that no longer exists.</summary>
    public static UserFacingMessage TransactionNotFound() => new("Validation.TransactionNotFound");

    /// <summary>A transaction whose amount is zero (TD-62).</summary>
    public static UserFacingMessage TransactionAmountZero() => new("Validation.Transaction.AmountZero");

    /// <summary>A rate entered for a pair whose two currencies are the same.</summary>
    public static UserFacingMessage ExchangeRateSameCurrency(string currency) => new("Validation.ExchangeRate.SameCurrency", currency);

    /// <summary>A rate that is zero or negative.</summary>
    public static UserFacingMessage ExchangeRateNotPositive() => new("Validation.ExchangeRate.NotPositive");

    /// <summary>A rate with more decimals than the store keeps.</summary>
    public static UserFacingMessage ExchangeRateTooPrecise(int maximumDecimals) => new("Validation.ExchangeRate.TooPrecise", maximumDecimals);

    /// <summary>A rate beyond the largest accepted.</summary>
    public static UserFacingMessage ExchangeRateTooLarge(decimal maximum) => new("Validation.ExchangeRate.TooLarge", maximum);

    /// <summary>A rate with no usable effective date.</summary>
    public static UserFacingMessage ExchangeRateDateRequired() => new("Validation.ExchangeRate.DateRequired");

    /// <summary>A base-currency change refused because a budget cannot be converted: no rate has been entered for its pair.</summary>
    public static UserFacingMessage BaseCurrencyRateMissing(string fromCurrency, string toCurrency)
        => new("Validation.BaseCurrency.RateMissing", fromCurrency, toCurrency);

    /// <summary>A base-currency change refused because converting a budget would leave it at zero.</summary>
    public static UserFacingMessage BaseCurrencyBudgetWouldBeZero(string category, string fromCurrency, string toCurrency)
        => new("Validation.BaseCurrency.BudgetWouldBeZero", category, fromCurrency, toCurrency);

    /// <summary>A currency code that is not ISO 4217.</summary>
    public static UserFacingMessage CurrencyCodeInvalid(string? code) => new("Validation.CurrencyCodeInvalid", code);

    /// <summary>A transaction in a currency other than its account's.</summary>
    public static UserFacingMessage CurrencyMismatch(string transactionCurrency, string accountCurrency)
        => new("Validation.CurrencyMismatch", transactionCurrency, accountCurrency);

    /// <summary>A receipt line whose quantity is zero or negative (TD-95).</summary>
    public static UserFacingMessage LineItemQuantityNotPositive(int lineNumber)
        => new("Validation.LineItem.QuantityNotPositive", lineNumber);

    /// <summary>A receipt line whose quantity has more decimal places than the store keeps (TD-95).</summary>
    public static UserFacingMessage LineItemQuantityScale(int lineNumber, int maximumDecimals)
        => new("Validation.LineItem.QuantityScale", lineNumber, maximumDecimals);

    /// <summary>A receipt whose line items do not sum to the total.</summary>
    public static UserFacingMessage LineItemsDoNotAddUp(decimal lineItemSum, decimal total, decimal difference)
        => new("Validation.LineItemsDoNotAddUp", lineItemSum, total, difference);

    /// <summary>An account whose ledger holds several currencies, so its balance has no total.</summary>
    public static UserFacingMessage AccountHoldsForeignCurrencies(string accountName, string foreignCurrencies, string accountCurrency)
        => new("Validation.AccountHoldsForeignCurrencies", accountName, foreignCurrencies, accountCurrency);

    /// <summary>An account whose recorded opening-balance currency is not the account's own (TD-77).</summary>
    public static UserFacingMessage OpeningBalanceCurrencyMismatch(string accountName, string openingCurrency, string accountCurrency)
        => new("Validation.OpeningBalanceCurrencyMismatch", accountName, openingCurrency, accountCurrency);

    /// <summary>An imported row in a currency other than the target account's.</summary>
    public static UserFacingMessage ImportedCurrencyMismatch(
        string payee, DateTimeOffset date, string transactionCurrency, string accountName, string accountCurrency)
        => new("Validation.ImportedCurrencyMismatch", payee, date, transactionCurrency, accountName, accountCurrency);

    /// <summary>An import file over the size the parsers are trusted with (TD-50).</summary>
    public static UserFacingMessage ImportFileTooLarge(int maximumMegabytes)
        => new("Validation.Import.FileTooLarge", maximumMegabytes);

    /// <summary>An import file whose content is not a spreadsheet or a text table (TD-50).</summary>
    public static UserFacingMessage ImportFileTypeNotSupported() => new("Validation.Import.FileTypeNotSupported");

    /// <summary>A manually attached file whose type is not on the allow-list (TD-141).</summary>
    public static UserFacingMessage AttachmentTypeNotSupported() => new("Validation.Attachment.TypeNotSupported");

    /// <summary>A manually attached file with no content (TD-141).</summary>
    public static UserFacingMessage AttachmentEmpty() => new("Validation.Attachment.Empty");

    /// <summary>A manually attached file over the attachment size limit (TD-141).</summary>
    public static UserFacingMessage AttachmentTooLarge(int maximumMegabytes)
        => new("Validation.Attachment.TooLarge", maximumMegabytes);

    /// <summary>A transfer whose two ends are one account.</summary>
    public static UserFacingMessage TransferSameAccount() => new("Validation.Transfer.SameAccount");

    /// <summary>A transfer of zero or less.</summary>
    public static UserFacingMessage TransferAmountNotPositive() => new("Validation.Transfer.AmountNotPositive");

    /// <summary>A transfer from an account that no longer exists.</summary>
    public static UserFacingMessage TransferSourceNotFound() => new("Validation.Transfer.SourceNotFound");

    /// <summary>A transfer to an account that no longer exists.</summary>
    public static UserFacingMessage TransferDestinationNotFound() => new("Validation.Transfer.DestinationNotFound");

    /// <summary>A transfer in a currency other than its source account's.</summary>
    public static UserFacingMessage TransferSourceCurrencyMismatch(string transferCurrency, string sourceCurrency)
        => new("Validation.Transfer.SourceCurrencyMismatch", transferCurrency, sourceCurrency);

    /// <summary>A transfer in a currency other than its destination account's.</summary>
    public static UserFacingMessage TransferDestinationCurrencyMismatch(string transferCurrency, string destinationCurrency)
        => new("Validation.Transfer.DestinationCurrencyMismatch", transferCurrency, destinationCurrency);

    /// <summary>A void with no reason.</summary>
    public static UserFacingMessage VoidReasonRequired() => new("Validation.Void.ReasonRequired");

    /// <summary>A void reason over the stored limit.</summary>
    public static UserFacingMessage VoidReasonTooLong(int maximumLength) => new("Validation.Void.ReasonTooLong", maximumLength);

    /// <summary>A void of a transaction that is already voided.</summary>
    public static UserFacingMessage VoidAlreadyVoided() => new("Validation.Void.AlreadyVoided");

    /// <summary>A void whose leg's account no longer exists.</summary>
    public static UserFacingMessage VoidAccountMissing() => new("Validation.Void.AccountMissing");

    /// <summary>A void whose leg is in a currency other than its account's.</summary>
    public static UserFacingMessage VoidCurrencyMismatch(string transactionCurrency, string accountCurrency)
        => new("Validation.Void.CurrencyMismatch", transactionCurrency, accountCurrency);

    /// <summary>An unvoid of a transaction that is not voided (TD-69).</summary>
    public static UserFacingMessage UnvoidNotVoided() => new("Validation.Unvoid.NotVoided");

    /// <summary>An unvoid whose leg's account no longer exists (TD-69).</summary>
    public static UserFacingMessage UnvoidAccountMissing() => new("Validation.Unvoid.AccountMissing");

    /// <summary>An unvoid whose leg is in a currency other than its account's (TD-69).</summary>
    public static UserFacingMessage UnvoidCurrencyMismatch(string transactionCurrency, string accountCurrency)
        => new("Validation.Unvoid.CurrencyMismatch", transactionCurrency, accountCurrency);

    /// <summary>An opening balance set on an account that already has one (TD-70).</summary>
    public static UserFacingMessage OpeningBalanceAlreadySet(string accountName)
        => new("Validation.OpeningBalance.AlreadySet", accountName);

    /// <summary>An opening balance with more decimal places than the account's currency has (TD-70).</summary>
    public static UserFacingMessage OpeningBalanceTooPrecise(string currency, int minorUnits)
        => new("Validation.OpeningBalance.TooPrecise", currency, minorUnits);

    /// <summary>An edit naming a transaction that has been voided (TD-68).</summary>
    public static UserFacingMessage TransactionVoided() => new("Validation.Transaction.Voided");

    /// <summary>An edit naming a transaction that is one leg of a transfer (TD-68).</summary>
    public static UserFacingMessage TransactionIsTransferLeg() => new("Validation.Transaction.IsTransferLeg");

    /// <summary>An edited transaction whose splits do not sum to the new total (TD-68).</summary>
    public static UserFacingMessage SplitsDoNotAddUp(decimal splitSum, decimal total, decimal difference)
        => new("Validation.SplitsDoNotAddUp", splitSum, total, difference);

    /// <summary>A rule with no name.</summary>
    public static UserFacingMessage RuleTitleRequired() => new("Validation.Rule.TitleRequired");

    /// <summary>A rule with no category to apply.</summary>
    public static UserFacingMessage RuleCategoryRequired() => new("Validation.Rule.CategoryRequired");

    /// <summary>A rule with no conditions, which would match everything.</summary>
    public static UserFacingMessage RuleConditionsRequired() => new("Validation.Rule.ConditionsRequired");

    /// <summary>A rule with more conditions than the engine evaluates.</summary>
    public static UserFacingMessage RuleTooManyConditions(int maximum) => new("Validation.Rule.TooManyConditions", maximum);

    /// <summary>A rule condition with nothing to compare against.</summary>
    public static UserFacingMessage RuleConditionValueRequired() => new("Validation.Rule.ConditionValueRequired");

    /// <summary>A password change with no current password.</summary>
    public static UserFacingMessage PasswordCurrentRequired() => new("Validation.Password.CurrentRequired");

    /// <summary>A password change with no new password.</summary>
    public static UserFacingMessage PasswordNewRequired() => new("Validation.Password.NewRequired");

    /// <summary>A new password under the vault's minimum strength.</summary>
    public static UserFacingMessage PasswordTooWeak() => new("Validation.Password.TooWeak");

    /// <summary>A new password identical to the current one.</summary>
    public static UserFacingMessage PasswordUnchanged() => new("Validation.Password.Unchanged");

    /// <summary>A budget that names no category, so there is nothing to measure it against.</summary>
    public static UserFacingMessage BudgetCategoryRequired() => new("Validation.Budget.CategoryRequired");

    /// <summary>A budget whose limit is zero or less.</summary>
    public static UserFacingMessage BudgetLimitNotPositive() => new("Validation.Budget.LimitNotPositive");

    /// <summary>A budget limit with more decimal places than its currency has.</summary>
    public static UserFacingMessage BudgetLimitTooPrecise(string currency, int minorUnits)
        => new("Validation.Budget.LimitTooPrecise", currency, minorUnits);

    /// <summary>A custom-dated budget missing one of its dates.</summary>
    public static UserFacingMessage BudgetCustomNeedsDates() => new("Validation.Budget.CustomNeedsDates");

    /// <summary>A fortnightly budget with no dates, whose window cannot be known.</summary>
    public static UserFacingMessage BudgetBiWeeklyNeedsDates() => new("Validation.Budget.BiWeeklyNeedsDates");

    /// <summary>A budget given one date without the other, which its window would ignore.</summary>
    public static UserFacingMessage BudgetDatesIncomplete() => new("Validation.Budget.DatesIncomplete");

    /// <summary>A budget whose start is not before its end.</summary>
    public static UserFacingMessage BudgetDatesOutOfOrder() => new("Validation.Budget.DatesOutOfOrder");

    /// <summary>A budget identical in category, currency and period to one that exists.</summary>
    /// <summary>A budget in a vault with no base currency (no account, no explicit setting).</summary>
    public static UserFacingMessage BudgetNoBaseCurrency() => new("Validation.Budget.NoBaseCurrency");

    public static UserFacingMessage BudgetAlreadyExists(string category, string currency)
        => new("Validation.Budget.AlreadyExists", category, currency);

    /// <summary>An import profile with no name, so there is nothing to show in the saved-profiles list.</summary>
    public static UserFacingMessage ImportProfileNameRequired() => new("Validation.ImportProfile.NameRequired");

    /// <summary>An import profile whose date format cannot parse a date at all.</summary>
    public static UserFacingMessage ImportProfileDateFormatInvalid() => new("Validation.ImportProfile.DateFormatInvalid");

    /// <summary>An import profile with no mapping for the two fields a transaction cannot do without.</summary>
    public static UserFacingMessage ImportProfileMappingIncomplete() => new("Validation.ImportProfile.MappingIncomplete");

    /// <summary>An update naming an import profile that was deleted, or never existed.</summary>
    public static UserFacingMessage ImportProfileNotFound() => new("Validation.ImportProfile.NotFound");

    /// <summary>A sync conflict resolution naming a conflict that is not recorded (post-audit plan item 3.1c).</summary>
    public static UserFacingMessage SyncConflictNotFound() => new("Validation.SyncConflict.NotFound");

    /// <summary>A sync conflict that was resolved already.</summary>
    public static UserFacingMessage SyncConflictAlreadyResolved() => new("Validation.SyncConflict.AlreadyResolved");

    /// <summary>Taking the other device's side would grant a consent, and that was not confirmed (§7).</summary>
    public static UserFacingMessage SyncConflictConsentNotConfirmed() => new("Validation.SyncConflict.ConsentNotConfirmed");

    /// <summary>A pairing with no offer file named (post-audit plan item 3.1e).</summary>
    public static UserFacingMessage SyncPairingOfferPathRequired() => new("Validation.SyncPairing.OfferPathRequired");

    /// <summary>A pairing offer asked to live for longer than an offer may.</summary>
    public static UserFacingMessage SyncPairingLifetimeOutOfRange() => new("Validation.SyncPairing.LifetimeOutOfRange");

    /// <summary>What was typed is not a pairing code: wrong length, or characters the alphabet has not got.</summary>
    public static UserFacingMessage SyncPairingCodeFormat() => new("Validation.SyncPairing.CodeFormat");

    /// <summary>A sync run with no folder named (post-audit plan item 3.1f).</summary>
    public static UserFacingMessage SyncRunFolderRequired() => new("Validation.SyncRun.FolderRequired");

    /// <summary>A sync folder given as a relative path, which would mean wherever Pine happened to be started.</summary>
    public static UserFacingMessage SyncRunFolderNotAbsolute() => new("Validation.SyncRun.FolderNotAbsolute");

    /// <summary>An idle auto-lock preference outside its allowed whole-minute range (TD-119).</summary>
    public static UserFacingMessage PreferenceAutoLockRange(int min, int max) => new("Validation.Preference.AutoLockRange", min, max);

    /// <summary>A categorisation confidence preference that is not a probability from 0 to 1 (TD-119).</summary>
    public static UserFacingMessage PreferenceConfidenceRange() => new("Validation.Preference.ConfidenceRange");

    /// <summary>A transactions page size preference outside its allowed range (TD-119).</summary>
    public static UserFacingMessage PreferencePageSizeRange(int min, int max) => new("Validation.Preference.PageSizeRange", min, max);
}
