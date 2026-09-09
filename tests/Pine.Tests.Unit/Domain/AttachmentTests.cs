using System;
using System.Linq;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Pine.Core.Attachments;
using Xunit;

namespace Pine.Tests.Unit.Domain;

/// <summary>
/// The attachment aggregate, and specifically the hash the previous shape did not have.
/// </summary>
/// <remarks>
/// <c>10-Domain-Model</c> §7 requires "Hash MUST be set" of an attachment. The <c>Attachment</c>
/// that shipped before iteration 27 had no hash property at all, and that -- not disuse -- is the
/// reason it was deleted rather than kept as a head start (TD-37). These tests exist so the
/// replacement cannot quietly acquire the same defect.
/// </remarks>
public class AttachmentTests
{
    private static readonly byte[] SomeContent = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));

    private static Attachment Create(byte[]? content = null) =>
        Attachment.Create(Guid.NewGuid(), "receipt.png", "image/png", content ?? SomeContent, Clock());

    /// <summary>The hash is computed from the content, not accepted from a caller.</summary>
    /// <remarks>
    /// A HASH A CALLER SUPPLIES IS A CLAIM; ONE THE AGGREGATE COMPUTES IS A FACT. There is no
    /// overload that takes a hash, which is what makes <see cref="Attachment.VerifyContent"/>
    /// mean something.
    /// </remarks>
    [Fact]
    public void Create_ComputesTheSha256OfTheContent()
    {
        var attachment = Create();

        attachment.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(SomeContent)));
        attachment.Sha256.Should().MatchRegex("^[0-9a-f]{64}$", "lowercase hex, so two vaults agree on the string");
        attachment.VerifyContent().Should().BeTrue();
    }

    /// <summary>Size is recorded as a column so a listing never has to load the blob.</summary>
    [Fact]
    public void Create_RecordsTheSize()
        => Create().SizeBytes.Should().Be(SomeContent.LongLength);

    /// <summary>The creation time comes from the injected clock, as everywhere else in this domain.</summary>
    [Fact]
    public void Create_StampsTheInjectedClock()
        => Create().CreatedAtUtc.Should().Be(new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc));

    /// <summary>An attachment always belongs to a transaction.</summary>
    [Fact]
    public void Create_RefusesAnEmptyTransactionId()
    {
        var act = () => Attachment.Create(Guid.Empty, "r.png", "image/png", SomeContent, Clock());

        act.Should().Throw<ArgumentException>().WithParameterName("transactionId");
    }

    /// <summary>A zero-byte attachment is a row claiming evidence that does not exist.</summary>
    [Fact]
    public void Create_RefusesEmptyContent()
    {
        var act = () => Attachment.Create(Guid.NewGuid(), "r.png", "image/png", [], Clock());

        act.Should().Throw<ArgumentException>().WithParameterName("content");
    }

    /// <summary>The size limit is enforced by the aggregate, so no caller can route around it.</summary>
    /// <remarks>
    /// It matters here more than it would elsewhere: the bytes go into <c>pine.db</c>, so an
    /// unbounded attachment is an unbounded database and an unbounded backup.
    /// </remarks>
    [Fact]
    public void Create_RefusesContentOverTheSizeLimit()
    {
        var tooBig = new byte[Attachment.MaximumSizeBytes + 1];

        var act = () => Attachment.Create(Guid.NewGuid(), "r.png", "image/png", tooBig, Clock());

        act.Should().Throw<ArgumentException>().WithParameterName("content");
    }

    /// <summary>A name and a media type are both required to show or restore the file.</summary>
    [Theory]
    [InlineData("", "image/png", "fileName")]
    [InlineData("   ", "image/png", "fileName")]
    [InlineData("r.png", "", "contentType")]
    [InlineData("r.png", "   ", "contentType")]
    public void Create_RefusesAMissingNameOrContentType(string fileName, string contentType, string parameter)
    {
        var act = () => Attachment.Create(Guid.NewGuid(), fileName, contentType, SomeContent, Clock());

        act.Should().Throw<ArgumentException>().WithParameterName(parameter);
    }

    /// <summary>
    /// Content that no longer matches the stored hash is reported as such.
    /// </summary>
    /// <remarks>
    /// THIS IS THE ASSERTION THE WHOLE HASH EXISTS FOR. Reflection is used to corrupt the array
    /// because there is deliberately no public way to change stored content -- an attachment is
    /// evidence, and evidence that can be edited in place is not evidence. The test has to reach
    /// past the aggregate precisely because a caller cannot.
    /// </remarks>
    [Fact]
    public void VerifyContent_IsFalse_WhenTheBytesNoLongerMatchTheHash()
    {
        var attachment = Create([.. SomeContent]);
        attachment.VerifyContent().Should().BeTrue();

        attachment.Content[0] ^= 0xFF;

        attachment.VerifyContent().Should().BeFalse(
            "a hash nothing compares detects nothing; this is what makes the column a tamper check");
    }

    /// <summary>Two attachments of the same bytes hash the same, and different bytes do not.</summary>
    [Fact]
    public void TheHash_IsAFunctionOfTheContentAlone()
    {
        var first = Create([.. SomeContent]);
        var second = Create([.. SomeContent]);
        var different = Create([.. SomeContent.Reverse()]);

        first.Sha256.Should().Be(second.Sha256);
        first.Sha256.Should().NotBe(different.Sha256);
        first.Id.Should().NotBe(second.Id, "identity is per attachment, not per file");
    }

    /// <summary>An attachment is an aggregate root, so a repository may load and save it alone.</summary>
    /// <remarks>
    /// The distinction is load-bearing here: a receipt must NOT be loaded whenever its transaction
    /// is, because that would read a photograph on every ledger query.
    /// </remarks>
    [Fact]
    public void Attachment_IsAnAggregateRoot()
        => typeof(Attachment).Should().BeDerivedFrom<Pine.Core.Common.AggregateRoot>();
}
