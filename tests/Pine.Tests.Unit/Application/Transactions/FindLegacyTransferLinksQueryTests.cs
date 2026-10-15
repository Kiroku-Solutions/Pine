using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Pine.Application.Transactions.Queries;
using Pine.Core.Transactions;
using Xunit;

namespace Pine.Tests.Unit.Application.Transactions;

/// <summary>
/// The read-only detector for transfer links that are not reciprocated (TD-124).
/// </summary>
public class FindLegacyTransferLinksQueryTests
{
    private static readonly Guid Account = Guid.NewGuid();

    private static TransferLinkRow Row(Guid id, Guid linked, bool partnerExists, Guid? partnerLink)
        => new(id, Account, linked, false, partnerExists, partnerLink);

    [Fact]
    public void AReciprocatedPair_IsNotReported()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var report = FindLegacyTransferLinksQueryHandler.Analyse(new[]
        {
            Row(a, b, true, a),
            Row(b, a, true, b),
        });

        report.IsClean.Should().BeTrue();
        report.LinkedRowCount.Should().Be(2);
    }

    [Fact]
    public void AOneWayLink_IsReportedOnTheRowThatPointsAtAPartnerWithNoLinkBack()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var report = FindLegacyTransferLinksQueryHandler.Analyse(new[] { Row(a, b, true, null) });

        report.Findings.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new LegacyTransferLinkFinding(a, Account, b, TransferLinkProblem.PartnerNotLinkedBack, false));
    }

    [Fact]
    public void DanglingMisdirectedAndSelfLinks_AreEachNamed()
    {
        var dangling = Guid.NewGuid();
        var misdirected = Guid.NewGuid();
        var self = Guid.NewGuid();

        var report = FindLegacyTransferLinksQueryHandler.Analyse(new[]
        {
            Row(dangling, Guid.NewGuid(), false, null),
            Row(misdirected, Guid.NewGuid(), true, Guid.NewGuid()),
            Row(self, self, true, self),
        });

        report.Findings.Should().HaveCount(3);
        report.Findings.Should().Contain(f => f.TransactionId == dangling && f.Problem == TransferLinkProblem.PartnerMissing);
        report.Findings.Should().Contain(f => f.TransactionId == misdirected && f.Problem == TransferLinkProblem.PartnerLinksElsewhere);
        report.Findings.Should().Contain(f => f.TransactionId == self && f.Problem == TransferLinkProblem.SelfLinked);
    }

    [Fact]
    public async Task TheHandler_OnlyReads_AndReportsWhatTheRepositoryAnswers()
    {
        var a = Guid.NewGuid();
        var repository = Substitute.For<ITransactionRepository>();
        repository.GetTransferLinkRowsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<TransferLinkRow>>(new[] { Row(a, Guid.NewGuid(), true, null) }));

        var report = await new FindLegacyTransferLinksQueryHandler(repository)
            .Handle(new FindLegacyTransferLinksQuery(), CancellationToken.None);

        report.Findings.Should().ContainSingle();
        await repository.DidNotReceiveWithAnyArgs().SaveAsync(default!, default);
        await repository.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }
}
