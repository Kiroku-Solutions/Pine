using FluentAssertions;
using Pine.Core.Security;
using Xunit;

namespace Pine.Tests.Unit.Security;

/// <summary>
/// TD-73: the estimator knows ordinary words, so a passphrase of them is priced as the few words it
/// is rather than as the many random letters it contains. The direction of every assertion is
/// "lower, never higher".
/// </summary>
public class PasswordStrengthNaturalWordTests
{
    [Fact]
    public void Evaluate_ShouldNotRateAFamousWordPassphraseLikeRandomCharacters()
    {
        const string passphrase = "correct horse battery staple";

        var phrase = PasswordStrengthPolicy.Evaluate(passphrase);

        // Random characters of the same length and class mix would score well over 100 bits.
        var random = PasswordStrengthPolicy.Evaluate("xKqmZvLwRtYbNcPdJsHfGeUa3k9o");
        phrase.EstimatedBits.Should().BeLessThan(random.EstimatedBits / 2);
        phrase.MeetsFloor.Should().BeFalse("four plain words and spaces are about 50 bits, under the 60-bit floor");
        phrase.Weaknesses.Should().Contain(PasswordWeakness.DictionaryWord);
    }

    [Fact]
    public void Evaluate_ShouldPriceMoreWordsHigherButStillFarBelowRandomLetters()
    {
        var four = PasswordStrengthPolicy.Evaluate("correct horse battery staple").EstimatedBits;
        var six = PasswordStrengthPolicy.Evaluate("correct horse battery staple garden river").EstimatedBits;

        six.Should().BeGreaterThan(four);
        six.Should().BeLessThan(four * 2);
    }

    [Theory]
    [InlineData("Correct-Horse-Battery-Staple")]
    [InlineData("correcthorsebatterystaple")]
    [InlineData("c0rrect h0rse battery st@ple")]
    public void Evaluate_ShouldSeeThroughCaseSeparatorsAndLeetspeak(string passphrase)
    {
        var result = PasswordStrengthPolicy.Evaluate(passphrase);

        result.MeetsFloor.Should().BeFalse();
        result.Weaknesses.Should().Contain(PasswordWeakness.DictionaryWord);
    }

    [Fact]
    public void Evaluate_ShouldRecogniseSpanishWords_IncludingAccents()
    {
        var plain = PasswordStrengthPolicy.Evaluate("caballo bonito tormenta ciudad");

        plain.MeetsFloor.Should().BeFalse();
        plain.Weaknesses.Should().Contain(PasswordWeakness.DictionaryWord);

        // The accented spelling must match the unaccented "montana" entry and score identically.
        var unaccented = PasswordStrengthPolicy.Evaluate("montana lluvia viento");
        var accented = PasswordStrengthPolicy.Evaluate("montaña lluvia viento");
        accented.Weaknesses.Should().Contain(PasswordWeakness.DictionaryWord);
        accented.EstimatedBits.Should().Be(unaccented.EstimatedBits);
    }

    [Theory]
    [InlineData("xK9#mQ2$vL")]
    [InlineData("7bTz!qW4nR#8")]
    public void Evaluate_ShouldNotPenaliseStrongPasswordsWithNoWords(string password)
    {
        var result = PasswordStrengthPolicy.Evaluate(password);

        result.MeetsFloor.Should().BeTrue();
        result.Weaknesses.Should().NotContain(PasswordWeakness.DictionaryWord);
    }

    [Fact]
    public void Evaluate_ShouldStillAcceptAPassphraseOfUncommonWordsPlusDigits()
    {
        // "thick" is on the list so it is priced as a word (129.8 bits, down from the 149.6 the audit
        // recorded before TD-73); the other words are not, and the whole still clears the floor.
        var result = PasswordStrengthPolicy.Evaluate("gravel-thicket-cinder-9271");

        result.MeetsFloor.Should().BeTrue();
        result.EstimatedBits.Should().BeLessThan(149.6, "the word list only ever lowers a score");
    }

    [Fact]
    public void Evaluate_ShouldNeverRaiseTheScore_BecauseOfTheWordList()
    {
        // Appending unexplained random characters to a word phrase must still be >= the phrase alone
        // (monotonicity holds with the new matcher), and a word phrase must not outscore the same
        // length of brute-forced letters.
        var phrase = PasswordStrengthPolicy.Evaluate("garden river mountain").EstimatedBits;
        var extended = PasswordStrengthPolicy.Evaluate("garden river mountain#Q").EstimatedBits;

        extended.Should().BeGreaterThanOrEqualTo(phrase);
        phrase.Should().BeLessThan(21 * 4.7d, "21 letters brute-forced at 26 lowercase would be ~98 bits");
    }

    [Fact]
    public void Evaluate_ShouldTreatTheEmbeddedListAsLoaded()
    {
        // If the embedded resource were missing, the matcher would silently do nothing. A single
        // dictionary word repeated through the separator path must register as a dictionary word.
        PasswordStrengthPolicy.Evaluate("horse horse horse").Weaknesses
            .Should().Contain(PasswordWeakness.DictionaryWord);
    }
}
