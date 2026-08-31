using System;
using FluentAssertions;
using Pine.Core.Importing;
using Xunit;

namespace Pine.Tests.Unit.Domain;

public class ImportKeyFactoryReceiptTests
{
    private static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Date = new(2026, 3, 5, 14, 30, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void The_same_receipt_derives_the_same_key()
    {
        var first = ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "ACME Coffee");
        var second = ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "  acme   coffee ");

        second.Should().Be(first);
    }

    [Fact]
    public void The_key_carries_the_receipt_scheme_prefix()
    {
        ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "ACME")
            .Should().StartWith("s1:");
    }

    [Fact]
    public void A_receipt_key_never_collides_with_an_import_key()
    {
        var receipt = ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "ACME");
        var derived = ImportKeyFactory.Derive(Date, -24.50m, "USD", "ACME", "statement.csv", 0);

        receipt.Should().NotBe(derived);
        receipt.Should().NotStartWith(ImportKeyFactory.DerivedSchemePrefix);
    }

    [Fact]
    public void The_time_of_day_does_not_change_the_key()
    {
        var morning = ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "ACME");
        var evening = ImportKeyFactory.FromReceipt(Account, Date.AddHours(6), -24.50m, "USD", "ACME");

        evening.Should().Be(morning);
    }

    [Theory]
    [InlineData("22222222-2222-2222-2222-222222222222", -24.50, "USD", "ACME")]
    [InlineData("11111111-1111-1111-1111-111111111111", -24.51, "USD", "ACME")]
    [InlineData("11111111-1111-1111-1111-111111111111", -24.50, "EUR", "ACME")]
    [InlineData("11111111-1111-1111-1111-111111111111", -24.50, "USD", "BETA")]
    public void Every_field_participates_in_the_key(string account, decimal amount, string currency, string merchant)
    {
        var baseline = ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "ACME");

        ImportKeyFactory.FromReceipt(Guid.Parse(account), Date, amount, currency, merchant)
            .Should().NotBe(baseline);
    }

    [Fact]
    public void The_key_fits_the_persisted_column()
    {
        ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", new string('x', 500))
            .Length.Should().BeLessThanOrEqualTo(ImportKeyFactory.MaxLength);
    }

    [Fact]
    public void The_sign_of_the_amount_is_significant()
    {
        var negative = ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "ACME");
        var positive = ImportKeyFactory.FromReceipt(Account, Date, 24.50m, "USD", "ACME");

        positive.Should().NotBe(negative);
    }

    [Fact]
    public void The_scale_of_the_amount_does_not_change_the_key()
    {
        var withScale = ImportKeyFactory.FromReceipt(Account, Date, -24.50m, "USD", "ACME");
        var withoutScale = ImportKeyFactory.FromReceipt(Account, Date, -24.5m, "USD", "ACME");

        withoutScale.Should().Be(withScale);
    }

    [Fact]
    public void A_missing_currency_is_refused()
    {
        var act = () => ImportKeyFactory.FromReceipt(Account, Date, -24.50m, " ", "ACME");

        act.Should().Throw<ArgumentException>();
    }
}
