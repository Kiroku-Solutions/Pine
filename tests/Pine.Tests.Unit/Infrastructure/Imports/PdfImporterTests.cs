using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pine.Core.Importing;
using Pine.Infrastructure.Importing;
using Xunit;

namespace Pine.Tests.Unit.Infrastructure.Importing;

public class PdfImporterTests
{
    [Fact]
    public async Task ImportAsync_WithInvalidPdf_ThrowsException()
    {
        // Arrange
        var timeProvider = TimeProvider.System;
        var importer = new PdfImporter(timeProvider);
        var profile = ImportProfile.Create("Test Profile", timeProvider);
        
        var dummyData = "Invalid PDF Data"; 
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(dummyData));
        
        // Act & Assert
        // UglyToad.PdfPig throws when parsing invalid PDFs
        await Assert.ThrowsAnyAsync<Exception>(() => importer.ImportAsync(stream, profile, CancellationToken.None));
    }
}
