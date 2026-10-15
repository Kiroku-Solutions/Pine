using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Pine.Infrastructure.Diagnostics;
using Xunit;

namespace Pine.Tests.Unit.Diagnostics;

/// <summary>
/// TD-82. The plaintext log sink is the only logger that exists before a vault key does, so it must
/// be strictly free of personal data. Free-text values (a payee, an account name, a passphrase) have
/// no shape a pattern can recognise, so they are refused by the NAME they are logged under.
/// </summary>
public class PreUnlockLogRedactionTests
{
    [Theory]
    [InlineData("passphrase=hunter2-correct-horse", "hunter2")]
    [InlineData("Unlock failed, Password: \"my secret phrase\"", "secret phrase")]
    [InlineData("Imported payee=Starbucks Reserve", "Starbucks")]
    [InlineData("accountName: Joint-Savings", "Joint-Savings")]
    public void Redact_ShouldMaskTheValueOfASensitiveKeyInMessageText(string message, string leaked)
    {
        var redacted = LogRedactor.Redact(message);

        redacted.Should().NotContain(leaked);
        redacted.Should().Contain(LogRedactor.SensitiveValuePlaceholder);
    }

    [Theory]
    [InlineData("payee=Starbucks Reserve Roastery", "payee=<redacted>")]
    [InlineData("Imported payee=Starbucks Reserve Roastery in 40 ms", "Imported payee=<redacted>")]
    [InlineData("payee=Starbucks Reserve Roastery status=ok", "payee=<redacted> status=ok")]
    [InlineData("memo: lunch with Ana  count=3", "memo: <redacted>  count=3")]
    [InlineData("payee=Cafe Zuleta, retries=2", "payee=<redacted>, retries=2")]
    [InlineData("payee=Cafe Zuleta\nnext line stays", "payee=<redacted>\nnext line stays")]
    [InlineData("payee=Cafe Zuleta   ", "payee=<redacted>   ")]
    public void Redact_ShouldMaskAnUnquotedMultiWordValueToTheNextKeyOrEndOfLine(string message, string expected)
    {
        LogRedactor.Redact(message).Should().Be(expected);
    }

    [Fact]
    public void Redact_ShouldStayLinearOnAHugeUnquotedValue()
    {
        var message = "payee=" + string.Concat(Enumerable.Repeat("word ", 50_000));

        var redacted = LogRedactor.Redact(message);

        redacted.Should().Be("payee=<redacted> ");
    }

    [Fact]
    public void Redact_ShouldLeaveOrdinaryDiagnosticsAlone()
    {
        LogRedactor.Redact("Vault unlock took 412 ms; KDF iterations 3").Should().Be("Vault unlock took 412 ms; KDF iterations 3");
    }

    [Fact]
    public void Log_ShouldNeverWritePersonalDataFromStructuredContext_WhenNoVaultIsOpen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PinePreUnlock", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var options = new PineLoggingOptions { Directory = directory, MinimumLevel = LogLevel.Trace };

            using (var provider = new PineFileLoggerProvider(options, TimeProvider.System))
            {
                provider.CreateLogger("Pine.Startup").Log(
                    LogLevel.Error,
                    new EventId(1, "vault.unlock_failed"),
                    new List<KeyValuePair<string, object?>>
                    {
                        new("Payee", "Cafe Zuleta"),
                        new("AccountName", "Family Emergency Fund"),
                        new("Passphrase", "tr0ub4dor&3"),
                        new("VaultName", "Camilo Household"),
                        new("Total", 1234.5m),
                        new("AttemptCount", 3),
                    },
                    exception: null,
                    (_, _) => "Unlock failed for passphrase=tr0ub4dor&3 in " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Vaults", "x.pine"));
            }

            var text = string.Join('\n', Directory.GetFiles(directory).SelectMany(File.ReadAllLines));

            text.Should().NotContain("Zuleta").And.NotContain("Emergency").And.NotContain("tr0ub4dor")
                .And.NotContain("Camilo Household").And.NotContain("1234").And.NotContain("Vaults");
            text.Should().Contain("\"AttemptCount\":3", "plain diagnostics must survive");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
