using System;
using FluentAssertions;
using Pine.Infrastructure.Diagnostics;
using Xunit;

namespace Pine.Tests.Unit.Diagnostics;

/// <summary>
/// Covers every rule of <see cref="LogRedactor"/>, the boundary that TD-25 turns on.
/// </summary>
/// <remarks>
/// <para>
/// EACH RULE HAS BOTH A POSITIVE AND A NEGATIVE CASE. A redactor that replaces everything passes
/// every "is it gone?" assertion and is useless, so each rule is also asserted NOT to fire on the
/// nearest lookalike it must tolerate: a version number next to an amount, a relative path next to
/// an absolute one, a Twitter-style handle next to an e-mail, a line number next to an account
/// number. Those negatives are what stop a future tightening of a rule from quietly erasing the
/// diagnostics the sink exists to carry.
/// </para>
/// <para>
/// The receipt-shaped case is the one that matters most: it is a verbatim reconstruction of what
/// the deleted <c>PaddleOcrEngine</c> trace used to print for a single receipt line.
/// </para>
/// </remarks>
public class LogRedactorTests
{
    [Fact]
    public void Redact_ShouldReturnNull_ForNull()
    {
        LogRedactor.Redact(null).Should().BeNull();
    }

    [Fact]
    public void Redact_ShouldReturnEmpty_ForEmpty()
    {
        LogRedactor.Redact(string.Empty).Should().BeEmpty();
    }

    // ===== §4.3 third clause: filesystem paths =====

    [Theory]
    [InlineData(@"Failed to open D:\vaults\family\pine.db")]
    [InlineData(@"Failed to open D:/vaults/family/pine.db")]
    [InlineData(@"Failed to open \\fileserver\vaults\pine.db")]
    public void Redact_ShouldReplaceAbsolutePaths_WithThePathPlaceholder(string message)
    {
        var redacted = LogRedactor.Redact(message);

        redacted.Should().Be("Failed to open " + LogRedactor.PathPlaceholder);
    }

    [Fact]
    public void Redact_ShouldReplaceAPosixAbsolutePath_WithThePathPlaceholder()
    {
        var redacted = LogRedactor.Redact("Failed to open /var/lib/pine/pine.db");

        redacted.Should().Be("Failed to open " + LogRedactor.PathPlaceholder);
    }

    /// <summary>
    /// The path-shaped NEGATIVE case: none of these is an absolute path, and a redactor that eats
    /// them destroys the message without protecting anything.
    /// </summary>
    [Theory]
    [InlineData("Reading Assets/Ai/ner_minilm_int8.onnx failed")]
    [InlineData("The import succeeded and/or was skipped")]
    [InlineData("Ratio 3/4 of rows imported")]
    public void Redact_ShouldNotTouch_StringsThatMerelyContainSlashes(string message)
    {
        var redacted = LogRedactor.Redact(message);

        redacted.Should().Be(message);
    }

    /// <summary>
    /// A path under the user's profile directory is reported as <c>&lt;home&gt;</c> rather than
    /// <c>&lt;path&gt;</c>: §4.3's fourth clause bans the profile NAME, and the profile directory
    /// contains it.
    /// </summary>
    [Fact]
    public void Redact_ShouldReplaceAPathUnderTheUserProfile_WithTheHomePlaceholder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Guard rather than assert: a CI image with no profile directory would make this vacuous,
        // and a vacuous pass is worse than a skip that is visible in the code.
        if (string.IsNullOrEmpty(home) || home.Length < 4)
        {
            return;
        }

        var message = "Vault at " + System.IO.Path.Combine(home, "Documents", "pine", "pine.db") + " is locked";

        var redacted = LogRedactor.Redact(message);

        redacted.Should().Be("Vault at " + LogRedactor.HomePlaceholder + " is locked");
        redacted.Should().NotContain(home);
    }

    /// <summary>A stack frame carries the source path of every frame built with a PDB. TD-25.</summary>
    [Fact]
    public void Redact_ShouldReplaceTheSourcePath_OfAStackFrame()
    {
        const string frame = @"   at Pine.Ui.Windows.Program.Main(String[] args) in D:\repos\Pine\src\Pine.Ui.Windows\Program.cs:line 66";

        var redacted = LogRedactor.Redact(frame);

        redacted.Should().NotContain("repos");
        redacted.Should().NotContain("Program.cs");
        redacted.Should().Contain(LogRedactor.PathPlaceholder);
        redacted.Should().StartWith("   at Pine.Ui.Windows.Program.Main(String[] args) in ");
    }

    // ===== §4.3 first clause: PII =====

    [Fact]
    public void Redact_ShouldReplaceAnEmailAddress()
    {
        var redacted = LogRedactor.Redact("Sync rejected for camilo.brazales+pine@example.co.uk today");

        redacted.Should().Be("Sync rejected for " + LogRedactor.EmailPlaceholder + " today");
    }

    [Fact]
    public void Redact_ShouldReplaceAnEmailAddress_EvenAtTheEndOfASentence()
    {
        var redacted = LogRedactor.Redact("Owner is user@example.com.");

        redacted.Should().Be("Owner is " + LogRedactor.EmailPlaceholder + ".");
    }

    /// <summary>The e-mail NEGATIVE case: an at-sign is not an address.</summary>
    [Theory]
    [InlineData("Retry @ next launch")]
    [InlineData("Handled by @maintainer")]
    public void Redact_ShouldNotTouch_AnAtSignThatIsNotAnAddress(string message)
    {
        LogRedactor.Redact(message).Should().Be(message);
    }

    // ===== §4.3 second clause: amounts =====

    [Theory]
    [InlineData("Total 1234.56 recorded", "Total <amount> recorded")]
    [InlineData("Total 45,90 recorded", "Total <amount> recorded")]
    public void Redact_ShouldReplaceMoneyShapedTokens(string message, string expected)
    {
        LogRedactor.Redact(message).Should().Be(expected);
    }

    /// <summary>
    /// The amount NEGATIVE case. A version, a duration and a date are all digits-with-punctuation
    /// and none of them is money; redacting them would gut the record shape §4.1 mandates.
    /// </summary>
    [Theory]
    [InlineData("Migrating vault from schema 1.2.3")]
    [InlineData("Inference took 12.345 ms")]
    [InlineData("Rebuilt the index on 2026-08-28")]
    public void Redact_ShouldNotTouch_NumbersThatAreNotAmounts(string message)
    {
        LogRedactor.Redact(message).Should().Be(message);
    }

    // ===== §4.3 first and fourth clauses: long digit runs =====

    [Fact]
    public void Redact_ShouldReplaceALongDigitRun()
    {
        LogRedactor.Redact("Account 4012888888881881 declined")
            .Should().Be("Account " + LogRedactor.DigitsPlaceholder + " declined");
    }

    /// <summary>The digit NEGATIVE case: five digits or fewer are counters, not identifiers.</summary>
    [Theory]
    [InlineData("Imported 12345 transactions")]
    [InlineData("Failed at line 66")]
    [InlineData("Wrote 50 MB")]
    public void Redact_ShouldNotTouch_ShortDigitRuns(string message)
    {
        LogRedactor.Redact(message).Should().Be(message);
    }

    // ===== The receipt-shaped case =====

    /// <summary>
    /// A verbatim reconstruction of what <c>PaddleOcrEngine.cs:107</c> used to write to the console
    /// for ONE region of ONE receipt. Even with the trace deleted, a string of this shape reaching a
    /// log by another route must lose its identifying parts.
    /// </summary>
    [Fact]
    public void Redact_ShouldStripEveryIdentifyingPart_OfAReceiptShapedTrace()
    {
        const string trace =
            "[OCR] Extracted: 'SUPERMAXI QUITO RUC 1790016919001 TOTAL 128.45 " +
            "facturacion@supermaxi.com.ec' (Conf: 0.98)";

        var redacted = LogRedactor.Redact(trace);

        redacted.Should().NotBeNull();
        redacted.Should().NotContain("1790016919001", "a tax id is PII under §4.3's first clause");
        redacted.Should().NotContain("128.45", "a receipt total is an amount under §4.3's second clause");
        redacted.Should().NotContain("supermaxi.com.ec", "an e-mail address is PII");
        redacted.Should().Contain(LogRedactor.DigitsPlaceholder);
        redacted.Should().Contain(LogRedactor.AmountPlaceholder);
        redacted.Should().Contain(LogRedactor.EmailPlaceholder);

        // The merchant name survives, and that is exactly why the OCR trace had to be DELETED rather
        // than redacted: no rule can recognise "SUPERMAXI" as a payee. This assertion records that
        // limitation rather than hiding it.
        redacted.Should().Contain("SUPERMAXI");
    }

    // ===== The never-throws guarantee =====

    [Theory]
    [InlineData("@")]
    [InlineData("/")]
    [InlineData("//")]
    [InlineData(@"\\")]
    [InlineData("C:")]
    [InlineData("C:\\")]
    [InlineData(".")]
    [InlineData("...")]
    [InlineData("1.")]
    [InlineData("1.2")]
    [InlineData(",99")]
    [InlineData("@.")]
    [InlineData("a@.")]
    public void Redact_ShouldNeverThrow_OnDegenerateInput(string message)
    {
        var act = () => LogRedactor.Redact(message);

        act.Should().NotThrow();
    }

    /// <summary>
    /// A crude but falsifiable stand-in for "never worse than O(n)": a pathological input that a
    /// backtracking regular expression would choke on is redacted in linear time. Asserted as a
    /// generous wall-clock ceiling rather than a precise measurement, because the failure this
    /// guards against is catastrophic backtracking -- seconds or minutes, not milliseconds.
    /// </summary>
    [Fact]
    public void Redact_ShouldStayLinear_OnPathologicalInput()
    {
        var pathological = new string('/', 20_000) + new string('1', 20_000) + new string('@', 20_000);

        var started = System.Diagnostics.Stopwatch.StartNew();
        var redacted = LogRedactor.Redact(pathological);
        started.Stop();

        redacted.Should().NotBeNull();
        started.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(2),
            "the redactor is a set of single forward passes and must not degrade on adversarial input");
    }
}
