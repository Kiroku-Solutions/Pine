using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pine.Core.Accounts;
using Pine.Core.Attachments;
using Pine.Core.Common.ValueObjects;
using Pine.Core.Security;
using Pine.Core.Transactions;
using Pine.Infrastructure.Data;
using Xunit;

namespace Pine.Tests.Integration.Data;

/// <summary>
/// Receipt storage, against a real SQLCipher vault on disk.
/// </summary>
/// <remarks>
/// <para>
/// THREE OF THESE CANNOT BE UNIT TESTS, AND THEY ARE THE THREE THAT MATTER. That the bytes are
/// encrypted at rest is a property of the file SQLCipher wrote, not of any C# object. That the
/// foreign key is enforced is a property of the running database — in SQLite a constraint does
/// nothing unless <c>PRAGMA foreign_keys</c> is on. And that a receipt survives a backup is a
/// property of what <c>VACUUM INTO</c> actually copies. Each was the deciding argument for putting
/// the blob in <c>pine.db</c> instead of in a file beside it, so each is checked rather than
/// assumed.
/// </para>
/// </remarks>
public class AttachmentStorageTests
{
    private static readonly byte[] ReceiptBytes =
        Encoding.UTF8.GetBytes("PINE-TEST-RECEIPT-CONTENT-that-is-recognisable-in-a-hex-dump");

    /// <summary>
    /// The system clock: these tests assert on stored bytes and constraints, never on a timestamp,
    /// so a fake clock would add a dependency without adding an assertion.
    /// </summary>
    private static TimeProvider Clock() => TimeProvider.System;

    private static async Task<(Guid TransactionId, Guid AttachmentId)> SeedAsync(TempVault vault)
    {
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);

        var account = Account.Create("Current", AccountType.Bank, new Money(0m, new CurrencyCode("USD")), Clock());
        context.Accounts.Add(account);

        var transaction = Transaction.Create(
            account.Id,
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            "Supermarket",
            new Money(-12.34m, new CurrencyCode("USD")),
            TransactionType.Expense,
            Clock());
        context.Transactions.Add(transaction);

        var attachment = Attachment.Create(transaction.Id, "receipt.png", "image/png", ReceiptBytes, Clock());
        context.Attachments.Add(attachment);

        await context.SaveChangesAsync(CancellationToken.None);
        return (transaction.Id, attachment.Id);
    }

    /// <summary>The bytes round-trip through a real vault unchanged, and still verify.</summary>
    [Fact]
    public async Task AReceipt_RoundTripsThroughTheVault_AndStillMatchesItsHash()
    {
        using var vault = new TempVault();
        var (_, attachmentId) = await SeedAsync(vault);

        await using var reopened = vault.CreateContext();
        var loaded = await reopened.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId);

        loaded.Should().NotBeNull();
        loaded!.Content.Should().Equal(ReceiptBytes);
        loaded.SizeBytes.Should().Be(ReceiptBytes.LongLength);
        loaded.VerifyContent().Should().BeTrue();
    }

    /// <summary>
    /// The receipt is encrypted at rest, which is what <c>10-Domain-Model</c> §6.2 and FR-TXN-02
    /// require.
    /// </summary>
    /// <remarks>
    /// THE ASSERTION IS ON THE FILE, NOT ON THE CODE. Reading <c>pine.db</c> as raw bytes and
    /// failing if the plaintext appears in it is the only check that would notice if the database
    /// were ever opened unkeyed, or if the blob were written somewhere else. A test that asked EF
    /// whether encryption was configured would pass in exactly the case that matters.
    /// </remarks>
    [Fact]
    public async Task AReceipt_IsNotReadableInTheDatabaseFile()
    {
        using var vault = new TempVault();
        await SeedAsync(vault);

        // Close every context so the WAL is checkpointed into the file being inspected.
        vault.CloseContexts();

        var raw = await File.ReadAllBytesAsync(vault.DatabasePath);

        IndexOf(raw, ReceiptBytes).Should().Be(
            -1,
            "the receipt must not be recoverable from the vault file without the key; SQLCipher "
            + "encrypts every page, and this is the assertion that would notice if it stopped");
    }

    /// <summary>
    /// An attachment cannot reference a transaction that does not exist.
    /// </summary>
    /// <remarks>
    /// The <c>Attachment</c> deleted in iteration 27 carried an unconstrained <c>Guid</c>, so a row
    /// could point at nothing at all (TD-37). This is the assertion that the replacement is
    /// different in the way that matters — and it has to run against the real provider, because a
    /// SQLite foreign key is inert unless <c>PRAGMA foreign_keys</c> is on.
    /// </remarks>
    [Fact]
    public async Task AnAttachmentForATransactionThatDoesNotExist_IsRefusedByTheDatabase()
    {
        using var vault = new TempVault();
        await using var context = vault.CreateContext();
        await context.Database.MigrateAsync(CancellationToken.None);

        context.Attachments.Add(
            Attachment.Create(Guid.NewGuid(), "orphan.png", "image/png", ReceiptBytes, Clock()));

        var act = async () => await context.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>(
            "an attachment is evidence FOR something; a row pointing at no transaction is the "
            + "dangling reference the previous shape allowed");
    }

    /// <summary>
    /// A receipt is inside the backup, because the backup copies the database.
    /// </summary>
    /// <remarks>
    /// THIS IS THE TEST THAT JUSTIFIES THE STORAGE DECISION. <c>VaultBackupService</c> backs the
    /// vault up by running <c>VACUUM INTO</c> on the keyed connection: it copies <c>pine.db</c> and
    /// nothing else. Had the receipt been written as a file beside the database — the shape TD-37
    /// recorded as the plan — it would be absent from every backup and every restore, silently, for
    /// the one feature whose entire purpose is an audit trail.
    /// </remarks>
    [Fact]
    public async Task AReceipt_SurvivesABackup()
    {
        using var vault = new TempVault();
        var (_, attachmentId) = await SeedAsync(vault);

        await using var context = vault.CreateContext();
        var service = new VaultBackupService(context);
        var backup = await service.CreateBackupAsync(
            vault.VaultDirectory,
            VaultBackupReason.Manual,
            CancellationToken.None);

        backup.Should().NotBeNull();

        // Through the vault, not directly: a context built with new PineDbContext holds a
        // connection string the vault cannot release, and its pooled handle then keeps the
        // backup file -- and so the whole vault directory -- alive past this test (TD-140).
        await using var restored = vault.CreateContext(backup!.Path);
        var loaded = await restored.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId);

        loaded.Should().NotBeNull("a backup that loses the evidence is not a backup of the ledger");
        loaded!.Content.Should().Equal(ReceiptBytes);
        loaded.VerifyContent().Should().BeTrue();
    }

    /// <summary>
    /// Listing attachments does not read the file content.
    /// </summary>
    /// <remarks>
    /// The blob lives in the same row as the metadata, so this is the difference between a
    /// transaction list that reads a paperclip and one that reads twenty photographs. The
    /// projection is the mechanism; this is the assertion that it is still a projection.
    /// </remarks>
    [Fact]
    public async Task ListingAttachments_DoesNotLoadTheContent()
    {
        using var vault = new TempVault();
        var (transactionId, attachmentId) = await SeedAsync(vault);

        await using var context = vault.CreateContext();
        var repository = new SqliteAttachmentRepository(context);

        var summaries = await repository.ListForTransactionAsync(transactionId, CancellationToken.None);

        summaries.Should().ContainSingle();
        summaries[0].Id.Should().Be(attachmentId);
        summaries[0].SizeBytes.Should().Be(ReceiptBytes.LongLength);

        context.ChangeTracker.Entries<Attachment>().Should().BeEmpty(
            "the listing projects into a summary and tracks nothing, so no Content array was "
            + "materialised on the way");
    }

    /// <summary>Removing an attachment leaves its transaction alone.</summary>
    /// <remarks>
    /// Pine voids ledger rows rather than deleting them. Removing evidence is a separate act and
    /// must not take the figure with it.
    /// </remarks>
    [Fact]
    public async Task RemovingAnAttachment_LeavesTheTransaction()
    {
        using var vault = new TempVault();
        var (transactionId, attachmentId) = await SeedAsync(vault);

        await using var context = vault.CreateContext();
        var repository = new SqliteAttachmentRepository(context);

        (await repository.RemoveAsync(attachmentId, CancellationToken.None)).Should().BeTrue();
        await context.SaveChangesAsync(CancellationToken.None);

        (await context.Attachments.CountAsync()).Should().Be(0);
        (await context.Transactions.AnyAsync(t => t.Id == transactionId)).Should().BeTrue();
    }

    /// <summary>Removing something already gone is the outcome the caller wanted.</summary>
    [Fact]
    public async Task RemovingAnAttachmentThatIsNotThere_IsNotAnError()
    {
        using var vault = new TempVault();
        await SeedAsync(vault);

        await using var context = vault.CreateContext();
        var repository = new SqliteAttachmentRepository(context);

        (await repository.RemoveAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeFalse();
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;

            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
