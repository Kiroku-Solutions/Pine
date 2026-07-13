using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Importing;
using Pine.Infrastructure.Importing;
using Xunit;

namespace Pine.Tests.Unit.Infrastructure.Importing;

public class CsvImporterTests
{
    [Fact]
    public async Task ImportAsync_WithEmptyFile_ReturnsEmptyList()
    {
        // Arrange
        var timeProvider = TimeProvider.System;
        var importer = new CsvImporter(timeProvider);
        var profile = ImportProfile.Create("Test Profile", timeProvider);
        var csvData = "Date,Amount,Merchant\n"; // Header only
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvData));
        
        var account = Pine.Core.Accounts.Account.Create("Test", Pine.Core.Accounts.AccountType.Bank, new Pine.Core.Common.ValueObjects.Money(0, new Pine.Core.Common.ValueObjects.CurrencyCode("USD")), timeProvider);
        
        // Act
        var result = await importer.ImportAsync(account, stream, profile, CancellationToken.None);
        
        // Assert
        Assert.Empty(result);
    }
}
