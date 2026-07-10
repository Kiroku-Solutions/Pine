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
        
        // Act
        var result = await importer.ImportAsync(stream, profile, CancellationToken.None);
        
        // Assert
        Assert.Empty(result);
    }
}
