using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Importing.Commands;
using Pine.Core.Importing;
using Xunit;

namespace Pine.Tests.Unit.Application.Importing;

/// <summary>
/// What a saved import profile is refused for, and in which words (TD-151).
/// </summary>
/// <remarks>
/// Until the wizard saved and re-used profiles, nothing sent <see cref="CreateImportProfileCommand"/>
/// or <see cref="UpdateImportProfileCommand"/>, so neither had a validator. Each refusal here is
/// asserted by its KEY -- the key is what the screen words in the user's language -- following the
/// same shape <c>CreateBudgetCommandValidatorTests</c> set for TD-152.
/// </remarks>
public class ImportProfileCommandValidatorTests
{
    private static Dictionary<string, string> ValidMappings() => new(StringComparer.Ordinal)
    {
        ["Date"] = "Date",
        ["Amount"] = "Amount",
    };

    private readonly IImportProfileRepository _repository = Substitute.For<IImportProfileRepository>();

    private static async Task<IReadOnlyList<string>> KeysFor(CreateImportProfileCommand command) =>
        (await new CreateImportProfileCommandValidator().ValidateAsync(command))
            .Select(error => error.UserFacingMessage.Key)
            .ToList();

    private async Task<IReadOnlyList<string>> KeysFor(UpdateImportProfileCommand command) =>
        (await new UpdateImportProfileCommandValidator(_repository).ValidateAsync(command))
            .Select(error => error.UserFacingMessage.Key)
            .ToList();

    [Fact]
    public async Task AUsableNewProfile_IsAccepted()
    {
        var command = new CreateImportProfileCommand("Chase Checking", "yyyy-MM-dd", ',', true, false, ValidMappings());

        (await KeysFor(command)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "Validation.ImportProfile.NameRequired")]
    [InlineData("   ", "Validation.ImportProfile.NameRequired")]
    public async Task ANewProfileWithNoName_IsRefused(string name, string expectedKey)
    {
        var command = new CreateImportProfileCommand(name, "yyyy-MM-dd", ',', true, false, ValidMappings());

        (await KeysFor(command)).Should().Contain(expectedKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-format")]
    [InlineData("yyyy-MM-ddTHH:mm:ssZZZZZ-not-real")]
    public async Task ANewProfileWithAnUnparseableDateFormat_IsRefused(string dateFormat)
    {
        var command = new CreateImportProfileCommand("Bank", dateFormat, ',', true, false, ValidMappings());

        (await KeysFor(command)).Should().Contain("Validation.ImportProfile.DateFormatInvalid");
    }

    [Fact]
    public async Task ANewProfileWithNoDateMapping_IsRefused()
    {
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal) { ["Amount"] = "Amount" };
        var command = new CreateImportProfileCommand("Bank", "yyyy-MM-dd", ',', true, false, mappings);

        (await KeysFor(command)).Should().Contain("Validation.ImportProfile.MappingIncomplete");
    }

    [Fact]
    public async Task ANewProfileWithNoAmountMapping_IsRefused()
    {
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal) { ["Date"] = "Date" };
        var command = new CreateImportProfileCommand("Bank", "yyyy-MM-dd", ',', true, false, mappings);

        (await KeysFor(command)).Should().Contain("Validation.ImportProfile.MappingIncomplete");
    }

    [Fact]
    public async Task AnUpdate_DoesNotRequireAName()
    {
        // UpdateImportProfileCommand carries no Name at all -- a saved profile is not renamed by it --
        // so the validator must not ask for one.
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ImportProfile?>(ImportProfile.Create("Bank", TimeProvider.System)));

        var command = new UpdateImportProfileCommand(id, "yyyy-MM-dd", ',', true, false, ValidMappings());

        (await KeysFor(command)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnUpdateOfAProfileThatDoesNotExist_IsRefused()
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ImportProfile?>(null));

        var command = new UpdateImportProfileCommand(id, "yyyy-MM-dd", ',', true, false, ValidMappings());

        (await KeysFor(command)).Should().Contain("Validation.ImportProfile.NotFound");
    }

    [Fact]
    public async Task AnUpdateWithBadSettings_IsRefused_EvenWhenTheProfileExists()
    {
        var id = Guid.NewGuid();
        _repository.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ImportProfile?>(ImportProfile.Create("Bank", TimeProvider.System)));

        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
        var command = new UpdateImportProfileCommand(id, "not-a-format", ',', true, false, mappings);

        var keys = await KeysFor(command);
        keys.Should().Contain("Validation.ImportProfile.DateFormatInvalid");
        keys.Should().Contain("Validation.ImportProfile.MappingIncomplete");
    }
}
