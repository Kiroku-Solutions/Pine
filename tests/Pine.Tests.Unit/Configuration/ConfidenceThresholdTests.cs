using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pine.Application.Transactions.Commands;
using Pine.Application.Transactions.Queries;
using Pine.Core.Accounts;
using Pine.Core.Ai;
using Pine.Core.Categories;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Configuration;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Configuration;

/// <summary>
/// The configured confidence gate changes what BOTH consumers do, not just what they are handed
/// (TD-27).
/// </summary>
/// <remarks>
/// <para>
/// THIS FILE EXISTS BECAUSE A FALSIFICATION MUTATION SURVIVED. The first version of this work
/// asserted, by reflection, that each handler <em>takes</em> <c>IOptions&lt;PineAiOptions&gt;</c>.
/// Reverting the import handler's comparison to the literal <c>0.3</c> it used to hold left that
/// assertion green: the handler still took the options and simply ignored them. Taking a dependency
/// and using it are different claims, and only the second one is the ticket.
/// </para>
/// <para>
/// Both consumers are covered with the SAME two thresholds, because the defect TD-27 names here is
/// specifically that the two numbers could drift apart — a test that pins only one of them
/// reproduces the original defect in the test suite.
/// </para>
/// </remarks>
public class ConfidenceThresholdTests
{
    private const string PredictedCategoryName = "Groceries";
    private const double PredictedConfidence = 0.5;

    /// <summary>
    /// The categoriser, predicting a NAME (TD-32).
    /// </summary>
    /// <remarks>
    /// This used to return a <see cref="Guid"/>, and the test then supplied a stubbed
    /// <c>ICategoryRepository</c> that resolved that id to a real <c>Category</c>. That stub is why
    /// these tests stayed green while the shipped path could not work: production resolved the same
    /// id against a <c>Categories</c> table that nothing has ever written a row to, so it always got
    /// <c>null</c> and always returned no suggestion. The test mocked away precisely the thing that
    /// was broken.
    /// </remarks>
    private static IAIProvider AiPredicting(double confidence)
    {
        var ai = Substitute.For<IAIProvider>();
        ai.CategorizeTransactionAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CategorisationResult(PredictedCategoryName, confidence)));

        return ai;
    }

    /// <summary>
    /// A confident prediction of NOTHING is still nothing (TD-32).
    /// </summary>
    /// <remarks>
    /// The categoriser returns an empty <c>Category</c> when it has no model, when the vault is
    /// locked, or when the stored model is unusable — and it pairs that with whatever confidence the
    /// scores happen to yield. Without this test, deleting the empty-string guard from the handler
    /// leaves every test green while the UI offers the user a blank category to accept. A
    /// falsification mutation did exactly that and survived, which is why this test exists.
    /// </remarks>
    [Fact]
    public async Task TheSuggestionPath_ShouldOfferNothing_WhenThePredictionIsEmptyHoweverConfident()
    {
        var ai = Substitute.For<IAIProvider>();
        ai.CategorizeTransactionAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CategorisationResult(string.Empty, 1.0)));

        var handler = new CategorizeTransactionQueryHandler(
            ai,
            Options.Create(new PineAiOptions { CategorisationConfidenceThreshold = 0.0 }));

        var suggestion = await handler.Handle(
            new CategorizeTransactionQuery("TESCO STORES", 12.50m), CancellationToken.None);

        suggestion.Should().BeNull(
            "an empty category is the categoriser's way of saying it has no answer. Offering it as a "
            + "suggestion would put a blank category in front of the user at full confidence");
    }

    /// <summary>
    /// The suggestion path offers a prediction only when it clears the configured threshold.
    /// </summary>
    [Theory]
    [InlineData(0.4, true)]
    [InlineData(0.9, false)]
    public async Task TheSuggestionPath_ShouldRespectTheConfiguredThreshold(double threshold, bool expectSuggestion)
    {
        var handler = new CategorizeTransactionQueryHandler(
            AiPredicting(PredictedConfidence),
            Options.Create(new PineAiOptions { CategorisationConfidenceThreshold = threshold }));

        var suggestion = await handler.Handle(
            new CategorizeTransactionQuery("SUPERMARKET LTD", 42m), CancellationToken.None);

        if (expectSuggestion)
        {
            suggestion.Should().Be(PredictedCategoryName);
        }
        else
        {
            suggestion.Should().BeNull("a prediction below the configured gate is not offered");
        }
    }

    /// <summary>
    /// The import path auto-applies a category only when it clears the SAME configured threshold.
    /// </summary>
    /// <remarks>
    /// The asymmetry between the two paths is what made the duplicated literal dangerous: import
    /// WRITES the category into the ledger, where the suggestion path merely shows it. A drift that
    /// left import's number lower would silently file transactions under a category the app
    /// considered too weak to display.
    /// </remarks>
    [Theory]
    [InlineData(0.4, true)]
    [InlineData(0.9, false)]
    public async Task TheImportPath_ShouldRespectTheSameConfiguredThreshold(double threshold, bool expectCategorised)
    {
        var account = Account.Create(
            "Everyday", AccountType.Bank, new Money(500m, new CurrencyCode("USD")), TimeProvider.System);

        var accounts = Substitute.For<IAccountRepository>();
        accounts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(account);

        var transactions = Substitute.For<ITransactionRepository>();
        transactions.GetExistingImportKeysAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>()));

        var parsed = Transaction.Create(
            account.Id,
            DateTimeOffset.UtcNow,
            "SUPERMARKET LTD",
            new Money(-25m, new CurrencyCode("USD")),
            TransactionType.Expense,
            TimeProvider.System);

        var importer = Substitute.For<IImporter>();
        importer.ImportAsync(Arg.Any<Account>(), Arg.Any<Stream>(), Arg.Any<ImportProfile>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>(new[] { parsed }));

        var handler = new ImportTransactionsCommandHandler(
            transactions,
            accounts,
            TimeProvider.System,
            AiPredicting(PredictedConfidence),
            NoRules(),
            Options.Create(new PineAiOptions { CategorisationConfidenceThreshold = threshold }));

        await handler.Handle(
            new ImportTransactionsCommand(
                account.Id,
                new MemoryStream(),
                importer,
                ImportProfile.Create("csv", TimeProvider.System)),
            CancellationToken.None);

        if (expectCategorised)
        {
            parsed.Splits.Should().ContainSingle()
                .Which.Category.Should().Be(PredictedCategoryName);
        }
        else
        {
            parsed.Splits.Should().BeEmpty("a prediction below the configured gate must not be written to the ledger");
        }
    }

    /// <summary>
    /// A rule repository with nothing in it.
    /// </summary>
    /// <remarks>
    /// These tests are about the CLASSIFIER's behaviour, so the rule engine must be silent: an
    /// empty rule set means <c>RuleEvaluator</c> returns no match and the handler falls through to
    /// the AI path, which is exactly what they assert on. Rules taking precedence over the model is
    /// asserted in <c>ImportRuleCategorisationTests</c>.
    /// </remarks>
    private static Pine.Core.Rules.IRuleRepository NoRules()
    {
        var rules = NSubstitute.Substitute.For<Pine.Core.Rules.IRuleRepository>();
        rules.GetEnabledAsync(Arg.Any<CancellationToken>())
            .Returns(System.Threading.Tasks.Task.FromResult<IReadOnlyList<Pine.Core.Rules.Rule>>(
                System.Array.Empty<Pine.Core.Rules.Rule>()));
        return rules;
    }
}
