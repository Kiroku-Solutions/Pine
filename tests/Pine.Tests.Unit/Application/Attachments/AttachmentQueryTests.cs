using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Attachments;
using Pine.Core.Attachments;
using Xunit;

namespace Pine.Tests.Unit.Application.Attachments;

/// <summary>
/// The read path, and specifically its refusal to hand back a receipt that no longer matches its
/// hash.
/// </summary>
/// <remarks>
/// <para>
/// THIS FILE EXISTS BECAUSE A SECOND MUTATION SURVIVED. Replacing the verification in
/// <c>GetAttachmentContentQueryHandler</c> with <c>if (false)</c> — so a tampered receipt would be
/// displayed as though it were the document that was scanned — left every attachment test green.
/// The aggregate's own <c>VerifyContent</c> was covered; the one call site that gives it
/// consequences was not, which makes the hash a column again rather than a tamper check.
/// </para>
/// </remarks>
public class AttachmentQueryTests
{
    private static readonly byte[] Original = [1, 2, 3, 4, 5, 6, 7, 8];

    private static Attachment Sound() =>
        Attachment.Create(Guid.NewGuid(), "receipt.png", "image/png", [.. Original], TimeProvider.System);

    private static IAttachmentRepository RepositoryReturning(Attachment? attachment)
    {
        var repository = Substitute.For<IAttachmentRepository>();
        repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(attachment));
        return repository;
    }

    /// <summary>A sound attachment is returned with its bytes.</summary>
    [Fact]
    public async Task AnIntactReceipt_IsReturned()
    {
        var attachment = Sound();
        var handler = new GetAttachmentContentQueryHandler(RepositoryReturning(attachment));

        var content = await handler.Handle(
            new GetAttachmentContentQuery(attachment.Id), CancellationToken.None);

        content.Should().NotBeNull();
        content!.Content.Should().Equal(Original);
        content.ContentType.Should().Be("image/png");
        content.FileName.Should().Be("receipt.png");
    }

    /// <summary>
    /// A receipt whose bytes no longer match its hash is refused, not shown.
    /// </summary>
    /// <remarks>
    /// REFUSING IS THE DECISION, AND IT IS NOT OBVIOUS. Returning the image with a warning beside
    /// it would invite a reader to believe the picture and skim the warning — and the entire point
    /// of keeping a receipt is that it can be trusted as evidence for the figure next to it. So the
    /// handler throws, and the exception implements <c>IUserFacingFailure</c> so the user is told
    /// what happened and that their transaction is unaffected.
    /// </remarks>
    [Fact]
    public async Task ATamperedReceipt_IsRefused()
    {
        var attachment = Sound();
        attachment.Content[0] ^= 0xFF;

        var handler = new GetAttachmentContentQueryHandler(RepositoryReturning(attachment));

        var act = async () => await handler.Handle(
            new GetAttachmentContentQuery(attachment.Id), CancellationToken.None);

        await act.Should().ThrowAsync<AttachmentCorruptedException>(
            "a hash whose mismatch changes nothing is decoration; this is the call site that gives "
            + "it consequences");
    }

    /// <summary>The refusal reaches the user as a sentence, not as a stack trace.</summary>
    [Fact]
    public void TheRefusal_CarriesAUserFacingSentence()
    {
        var exception = new AttachmentCorruptedException();

        exception.Should().BeAssignableTo<Pine.Core.Common.IUserFacingFailure>();
        exception.UserFacingMessage.Should().NotBeNullOrWhiteSpace();
        exception.UserFacingMessage.Should().Contain(
            "transaction",
            "the user needs to know the ledger is intact and only the evidence is suspect");
    }

    /// <summary>An attachment that is not there is null, not an error.</summary>
    [Fact]
    public async Task AMissingAttachment_IsNull()
    {
        var handler = new GetAttachmentContentQueryHandler(RepositoryReturning(null));

        var content = await handler.Handle(
            new GetAttachmentContentQuery(Guid.NewGuid()), CancellationToken.None);

        content.Should().BeNull();
    }

    /// <summary>Removing an attachment that is not there is the outcome the caller wanted.</summary>
    [Fact]
    public async Task RemovingSomethingAlreadyGone_IsNotAnError()
    {
        var repository = Substitute.For<IAttachmentRepository>();
        repository.RemoveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var handler = new RemoveAttachmentCommandHandler(repository);

        (await handler.Handle(new RemoveAttachmentCommand(Guid.NewGuid()), CancellationToken.None))
            .Should().BeFalse();
    }

    /// <summary>Removing an attachment is transactional, like every other write here.</summary>
    [Fact]
    public void RemovingAnAttachment_GoesThroughTheTransactionPipeline()
        => new RemoveAttachmentCommand(Guid.NewGuid())
            .Should().BeAssignableTo<Pine.Application.Common.ITransactionalRequest>();
}
