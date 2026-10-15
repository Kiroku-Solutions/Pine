using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Pine.Application.Settings;
using Pine.Application.Settings.Commands;
using Xunit;

namespace Pine.Tests.Unit.Application.Settings;

/// <summary>TD-119: the three operative preferences are range-checked when saved, with a localisable refusal.</summary>
public class UserPreferenceValidationTests
{
    private static Task<System.Collections.Generic.IReadOnlyList<Pine.Application.Common.ValidationError>> Validate(string key, string value) =>
        new SaveSettingCommandValidator().ValidateAsync(new SaveSettingCommand(key, value), CancellationToken.None);

    [Theory]
    [InlineData(UserPreferenceKeys.AutoLockIdleMinutes, "1")]
    [InlineData(UserPreferenceKeys.AutoLockIdleMinutes, "480")]
    [InlineData(UserPreferenceKeys.CategorisationConfidenceThreshold, "0")]
    [InlineData(UserPreferenceKeys.CategorisationConfidenceThreshold, "0.35")]
    [InlineData(UserPreferenceKeys.CategorisationConfidenceThreshold, "1")]
    [InlineData(UserPreferenceKeys.TransactionsPageSize, "1")]
    [InlineData(UserPreferenceKeys.TransactionsPageSize, "500")]
    public async Task InRangeValues_AreAccepted(string key, string value) =>
        (await Validate(key, value)).Should().BeEmpty();

    [Theory]
    [InlineData(UserPreferenceKeys.AutoLockIdleMinutes, "0", "Validation.Preference.AutoLockRange")]
    [InlineData(UserPreferenceKeys.AutoLockIdleMinutes, "481", "Validation.Preference.AutoLockRange")]
    [InlineData(UserPreferenceKeys.AutoLockIdleMinutes, "ten", "Validation.Preference.AutoLockRange")]
    [InlineData(UserPreferenceKeys.AutoLockIdleMinutes, "", "Validation.Preference.AutoLockRange")]
    [InlineData(UserPreferenceKeys.CategorisationConfidenceThreshold, "-0.1", "Validation.Preference.ConfidenceRange")]
    [InlineData(UserPreferenceKeys.CategorisationConfidenceThreshold, "1.01", "Validation.Preference.ConfidenceRange")]
    [InlineData(UserPreferenceKeys.CategorisationConfidenceThreshold, "NaN", "Validation.Preference.ConfidenceRange")]
    [InlineData(UserPreferenceKeys.CategorisationConfidenceThreshold, "0,3", "Validation.Preference.ConfidenceRange")]
    [InlineData(UserPreferenceKeys.TransactionsPageSize, "0", "Validation.Preference.PageSizeRange")]
    [InlineData(UserPreferenceKeys.TransactionsPageSize, "501", "Validation.Preference.PageSizeRange")]
    [InlineData(UserPreferenceKeys.TransactionsPageSize, "1.5", "Validation.Preference.PageSizeRange")]
    public async Task OutOfRangeValues_AreRefused_WithTheMatchingLocalisedKey(string key, string value, string messageKey)
    {
        var errors = await Validate(key, value);

        errors.Should().ContainSingle().Which.UserFacingMessage.Key.Should().Be(messageKey);
    }

    [Fact]
    public async Task OtherKeys_AreUnaffected() =>
        (await Validate("Appearance.Theme", "0")).Should().BeEmpty();
}
