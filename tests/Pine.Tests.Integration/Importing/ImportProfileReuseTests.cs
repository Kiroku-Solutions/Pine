using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Pine.Application.Importing.Commands;
using Pine.Application.Importing.Queries;
using Pine.Application.Transactions.Commands;
using Pine.Core.Importing;
using Pine.Core.Transactions;
using Pine.Infrastructure.Importing;
using Pine.Tests.Integration.Atomicity;
using Xunit;

namespace Pine.Tests.Integration.Importing;

/// <summary>
/// End-to-end proof of TD-151 (a saved profile is reused, not re-typed) and TD-66 (the import key it
/// produces stays stable across that reuse), against a real SQLCipher vault driven through the real
/// MediatR pipeline -- the same proof shape <c>ImportDeduplicationTests</c> uses for the importers.
/// </summary>
/// <remarks>
/// <para>
/// THE SCENARIO THESE TESTS EXIST TO COVER. Before TD-151, <c>CreateImportProfileCommand</c>,
/// <c>UpdateImportProfileCommand</c> and <c>GetImportProfilesQuery</c> compiled, were registered,
/// and were sent by nothing: <c>ImportWizardViewModel</c> minted a throwaway profile on every
/// import and discarded it. A profile saved here, reused for a second import of the same statement,
/// and the account's keys checked against the file afterwards is the one proof that both halves --
/// "the mapping is actually saved" and "reusing it does not break duplicate detection" -- hold
/// together, which neither a wizard unit test (no real vault) nor <c>ImportDeduplicationTests</c>
/// (never sends the three requests) can give by itself.
/// </para>
/// <para>
/// WHY THE SECOND IMPORT MUST SAVE NOTHING. That is TD-66 made concrete: the importer derives the
/// import key from <see cref="ImportProfile.Id"/>, which <see cref="CreateImportProfileCommand"/>'s
/// handler assigns once and <see cref="UpdateImportProfileCommand"/> never changes. Re-running the
/// same file through the SAME saved profile must reproduce the keys already on file, so the second
/// import is a no-op; if the key depended on the profile's NAME instead -- the bug this ticket
/// closes -- it would still happen to pass here, because nothing below renames the profile. What
/// would catch that regression is <c>CsvImporterImportKeyTests</c>'s
/// <c>ImportAsync_WithTwoDifferentProfilesSharingTheSameName_ProducesDifferentKeys</c>; this test's
/// job is the saved/reused round trip, not the key derivation itself.
/// </para>
/// </remarks>
public class ImportProfileReuseTests
{
    private const decimal OpeningBalance = 1000m;
    private const string Csv = "Date,Payee,Amount\n2026-03-04,Coffee Hut,-4.50\n2026-03-05,Bookshop,-12.00\n";

    private static Dictionary<string, string> Mappings() => new(StringComparer.Ordinal)
    {
        ["Date"] = "Date",
        ["Payee"] = "Payee",
        ["Amount"] = "Amount",
    };

    private static async Task<ImportTransactionsResult> ImportAsync(AtomicityHost host, Guid account, ImportProfile profile)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Csv));
        var importer = new CsvImporter(TimeProvider.System);
        return await host.Mediator.Send(new ImportTransactionsCommand(account, stream, importer, profile));
    }

    [Fact]
    public async Task ASavedProfile_IsOfferedBack_AndReusingItImportsIdenticallyToTheFirstTime()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", OpeningBalance);

        // 1. The wizard's "save mapping as profile" step, for real: CreateImportProfileCommand is
        //    sent and persisted, not merely built and thrown away.
        var created = await host.Mediator.Send(new CreateImportProfileCommand(
            "Acme Bank Checking", "yyyy-MM-dd", ',', HasHeaderRecord: true, InvertAmounts: false, Mappings()));

        created.Id.Should().NotBe(Guid.Empty);

        // 2. The first import, under the saved profile.
        var first = await ImportAsync(host, account, created);
        first.Imported.Should().HaveCount(2);
        host.CountRowsRaw("Transactions").Should().Be(2);

        // 3. A LATER SESSION: the wizard opens again and lists saved profiles exactly as
        //    GetImportProfilesQuery now has a real sender for. The profile it gets back is a fresh
        //    instance, read from the database -- not the in-memory object step 1 returned -- which is
        //    what actually proves persistence rather than an identity-map illusion.
        var listed = await host.Mediator.Send(new GetImportProfilesQuery());
        var reused = listed.Should().ContainSingle(p => p.Name == "Acme Bank Checking").Subject;
        reused.Id.Should().Be(created.Id, "the same saved profile must come back, not a copy with a new identity");
        reused.ColumnMappings.Should().BeEquivalentTo(Mappings(),
            "the mapping is what makes 'reuse' meaningful -- it must not have to be re-entered");

        // 4. The SECOND import, reusing the profile the list handed back. Because it is the SAME
        //    saved profile (same id), it must derive the SAME import keys as the first import and
        //    therefore save nothing -- the TD-66 property this whole test exists to pin.
        var second = await ImportAsync(host, account, reused);

        second.Imported.Should().BeEmpty("every row the second import parsed was already imported under this profile");
        second.Rows.Should().HaveCount(2);
        second.Rows.Select(row => row.Disposition).Should()
            .AllBeEquivalentTo(ImportRowDisposition.SkippedAlreadyInAccount);

        host.CountRowsRaw("Transactions").Should().Be(2, "reusing the saved profile must not duplicate the ledger");
        (await host.ReadBalanceAsync(account)).Should().Be(OpeningBalance - 16.50m);
    }

    [Fact]
    public async Task UpdatingAReusedProfile_KeepsItsIdentity_SoDuplicateDetectionSurvivesTheChange()
    {
        await using var host = await AtomicityHost.CreateAsync();
        var account = await host.SeedAccountAsync("Everyday", OpeningBalance);

        var created = await host.Mediator.Send(new CreateImportProfileCommand(
            "Acme Bank Checking", "yyyy-MM-dd", ',', HasHeaderRecord: true, InvertAmounts: false, Mappings()));

        var first = await ImportAsync(host, account, created);
        first.Imported.Should().HaveCount(2);

        // The user re-opens the saved profile and tweaks a setting -- this is
        // UpdateImportProfileCommand's one real sender, the mapping step's "Save" button acting on a
        // profile that is already loaded.
        var updated = await host.Mediator.Send(new UpdateImportProfileCommand(
            created.Id, "yyyy-MM-dd", ',', HasHeaderRecord: true, InvertAmounts: false, Mappings()));

        updated.Id.Should().Be(created.Id, "an update changes settings, never identity");

        // Re-importing the same statement under the UPDATED profile must still recognise every row
        // as already-imported: the update must not have minted a new id under the covers.
        var second = await ImportAsync(host, account, updated);

        second.Imported.Should().BeEmpty();
        second.Rows.Select(row => row.Disposition).Should()
            .AllBeEquivalentTo(ImportRowDisposition.SkippedAlreadyInAccount);
        host.CountRowsRaw("Transactions").Should().Be(2);
    }
}
