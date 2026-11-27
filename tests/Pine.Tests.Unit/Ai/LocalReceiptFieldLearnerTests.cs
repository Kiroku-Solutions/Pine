using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Pine.Core.Ai;
using Pine.Core.Security;
using Pine.Infrastructure.Ai.Extraction;
using Pine.Infrastructure.Security;
using Xunit;

namespace Pine.Tests.Unit.Ai;

public sealed class LocalReceiptFieldLearnerTests : IDisposable
{
    private readonly string _vaultDir = Path.Combine(Path.GetTempPath(), $"pine-receipt-learner-{Guid.NewGuid():N}");
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public LocalReceiptFieldLearnerTests()
    {
        Directory.CreateDirectory(_vaultDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_vaultDir))
        {
            Directory.Delete(_vaultDir, recursive: true);
        }
    }

    [Fact]
    public void ReceiptBoxExtractor_ExtractsTextAndLeftAnchorFromSpanishReceipt()
    {
        var words = new List<OcrWord>
        {
            Word("CORPORACION", 100, 40, 220, 30),
            Word("FAVORITA", 340, 40, 180, 30),
            Word("VALOR", 80, 760, 100, 28),
            Word("TOTAL", 190, 760, 100, 28),
            Word("A", 300, 760, 25, 28),
            Word("PAGAR", 335, 760, 110, 28),
            Word("$42,85", 720, 760, 140, 28),
        };

        var totalBox = NormalizedRect.CreateClamped(0.68, 0.74, 0.90, 0.81);
        var extracted = ReceiptBoxExtractor.Extract(ReceiptTagField.Total, totalBox, words, 1000, 1000);

        extracted.ExtractedText.Should().Be("$42,85");
        extracted.LeftAnchorText.Should().Contain("PAGAR");
        ReceiptBoxExtractor.TryParseAmount(extracted.ExtractedText, DecimalSeparator.Comma).Should().Be(42.85m);
    }

    [Fact]
    public async Task LocalReceiptFieldLearner_LearnsSpanishFacturaTemplate_PersistsEncrypted_AndOverridesWeakBaseline()
    {
        var vaultAccessor = new StubVaultAccessor(_vaultDir, _key);
        var cipher = new AesGcmVaultFileCipher(vaultAccessor);

        using var learner = new LocalReceiptFieldLearner(vaultAccessor, cipher);

        // User tags a Spanish Ecuadorian factura once
        var annotation = new ReceiptLayoutAnnotation(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "FARMACIA FYBECA",
            new[]
            {
                new ReceiptFieldBox(
                    ReceiptTagField.Merchant,
                    NormalizedRect.CreateClamped(0.10, 0.04, 0.70, 0.09),
                    "FARMACIA FYBECA"),
                new ReceiptFieldBox(
                    ReceiptTagField.Total,
                    NormalizedRect.CreateClamped(0.70, 0.78, 0.92, 0.83),
                    "38,40",
                    LeftAnchorText: "IMPORTE NETO PAGAR"),
                new ReceiptFieldBox(
                    ReceiptTagField.Date,
                    NormalizedRect.CreateClamped(0.55, 0.16, 0.85, 0.21),
                    "14/08/2026",
                    LeftAnchorText: "EMISION"),
                new ReceiptFieldBox(
                    ReceiptTagField.Tax,
                    NormalizedRect.CreateClamped(0.70, 0.71, 0.92, 0.76),
                    "4,80",
                    LeftAnchorText: "IVA 15%"),
            });

        var status = await learner.RecordAnnotationAndTrainInBackgroundAsync(annotation);
        status.SampleCount.Should().Be(1);

        // Flush deterministic training and verify encrypted file was written (not plaintext)
        await learner.TrainNowAsync();
        var modelPath = Path.Combine(_vaultDir, LocalReceiptFieldLearner.ModelDirectoryName, LocalReceiptFieldLearner.ModelFileName);
        File.Exists(modelPath).Should().BeTrue();
        var rawBytes = await File.ReadAllBytesAsync(modelPath);
        System.Text.Encoding.UTF8.GetString(rawBytes).Should().NotContain("FYBECA");

        // Create a brand new learner instance to verify encrypted reload from disk
        using var reloaded = new LocalReceiptFieldLearner(vaultAccessor, cipher);
        reloaded.GetStatus().SampleCount.Should().Be(1);

        // New receipt from the same merchant with a weak/missing heuristic baseline
        var newReceiptWords = new List<OcrWord>
        {
            Word("FARMACIA", 120, 50, 200, 30),
            Word("FYBECA", 340, 50, 180, 30),
            Word("EMISION:", 150, 175, 160, 28),
            Word("21/09/2026", 580, 175, 220, 28),
            Word("IVA", 150, 725, 80, 28),
            Word("15%", 240, 725, 80, 28),
            Word("6,00", 740, 725, 120, 28),
            Word("IMPORTE", 120, 795, 160, 28),
            Word("NETO", 290, 795, 110, 28),
            Word("PAGAR", 410, 795, 130, 28),
            Word("46,00", 740, 795, 130, 28),
        };

        var weakBaseline = new ReceiptExtractionResult(
            Merchant: "Unknown Merchant",
            TotalAmount: 0m,
            CurrencyCode: "USD",
            Date: null,
            Confidence: ReceiptConfidence.None);

        var improved = reloaded.ApplyLearnedModel(weakBaseline, newReceiptWords, 1000, 1000);

        improved.Merchant.Should().Be("FARMACIA FYBECA");
        improved.TotalAmount.Should().Be(46.00m);
        improved.Date.Should().NotBeNull();
        improved.Date!.Value.Year.Should().Be(2026);
        improved.Date!.Value.Month.Should().Be(9);
        improved.Date!.Value.Day.Should().Be(21);
        improved.TaxAmount.Should().Be(6.00m);
    }

    private static OcrWord Word(string text, float x, float y, float w, float h) =>
        new()
        {
            Text = text,
            Confidence = 0.95f,
            BoundingBox = new BoundingBox { X = x, Y = y, Width = w, Height = h },
        };

    private sealed class StubVaultAccessor(string dbPath, byte[] key) : IVaultDataKeyAccessor
    {
        public bool IsUnlocked => true;
        public byte[]? Key => key;
        public string? DbPath => dbPath;
    }
}
