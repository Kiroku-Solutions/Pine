using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Attachments;
using Pine.Core.Attachments;
using Pine.Core.Common;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Attachments;

/// <summary>TD-141: the manual attachment path, its allow-list and its size limit.</summary>
public class AttachFileToTransactionTests
{
    private static readonly byte[] Bytes = [1, 2, 3, 4];

    private static Transaction SomeTransaction() => Transaction.Create(
        Guid.NewGuid(),
        new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
        "ACME",
        new Money(-10m, new CurrencyCode("USD")),
        TransactionType.Expense,
        TimeProvider.System);

    [Theory]
    [InlineData("contract.pdf", "application/pdf")]
    [InlineData("photo.JPG", "image/jpeg")]
    [InlineData("scan.png", "image/png")]
    public void AllowListedExtensions_MapToTheirMediaType(string name, string expected)
        => AttachmentFileGuard.MediaTypeFor(name).Should().Be(expected);

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    [InlineData("")]
    public void ExtensionsOffTheAllowList_AreRefused(string name)
    {
        AttachmentFileGuard.MediaTypeFor(name).Should().BeNull();
        AttachmentFileGuard.Inspect(name, 10)!.UserFacingMessage.Key.Should().Be("Validation.Attachment.TypeNotSupported");
    }

    [Fact]
    public void AnEmptyFile_IsRefused()
        => AttachmentFileGuard.Inspect("a.pdf", 0)!.UserFacingMessage.Key.Should().Be("Validation.Attachment.Empty");

    [Fact]
    public void TheSizeLimitIsInclusive()
    {
        AttachmentFileGuard.Inspect("a.pdf", Attachment.MaximumSizeBytes).Should().BeNull();

        var over = AttachmentFileGuard.Inspect("a.pdf", Attachment.MaximumSizeBytes + 1);
        over!.UserFacingMessage.Key.Should().Be("Validation.Attachment.TooLarge");
        over.UserFacingMessage.Arguments.Should().Equal(25);
    }

    [Fact]
    public async Task TheValidator_RefusesAMissingTransaction()
    {
        var transactions = Substitute.For<ITransactionRepository>();
        transactions.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Transaction?)null);
        var validator = new AttachFileToTransactionCommandValidator(transactions);

        var errors = await validator.ValidateAsync(new AttachFileToTransactionCommand(Guid.NewGuid(), "a.pdf", Bytes));

        errors.Select(e => e.UserFacingMessage.Key).Should().Contain("Validation.TransactionNotFound");
    }

    [Fact]
    public async Task TheValidator_AcceptsAGoodFileOnAnExistingTransaction()
    {
        var transaction = SomeTransaction();
        var transactions = Substitute.For<ITransactionRepository>();
        transactions.GetByIdAsync(transaction.Id, Arg.Any<CancellationToken>()).Returns(transaction);
        var validator = new AttachFileToTransactionCommandValidator(transactions);

        (await validator.ValidateAsync(new AttachFileToTransactionCommand(transaction.Id, "a.pdf", Bytes)))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task TheHandler_StoresTheFileWithADerivedTypeAndAHash()
    {
        var repository = Substitute.For<IAttachmentRepository>();
        Attachment? stored = null;
        repository.AddAsync(Arg.Do<Attachment>(a => stored = a), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var handler = new AttachFileToTransactionCommandHandler(repository, TimeProvider.System);
        var transactionId = Guid.NewGuid();

        var id = await handler.Handle(
            new AttachFileToTransactionCommand(transactionId, @"C:\docs\Lease.PDF", Bytes), CancellationToken.None);

        stored.Should().NotBeNull();
        stored!.Id.Should().Be(id);
        stored.TransactionId.Should().Be(transactionId);
        stored.FileName.Should().Be("Lease.PDF");
        stored.ContentType.Should().Be("application/pdf");
        stored.VerifyContent().Should().BeTrue();
    }
}
