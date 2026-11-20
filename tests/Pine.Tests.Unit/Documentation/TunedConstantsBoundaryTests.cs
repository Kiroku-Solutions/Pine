using FluentAssertions;

namespace Pine.Tests.Unit.Documentation;

/// <summary>
/// TD-118: the vision/OCR constants are deliberately literals. The reason must stay written where a
/// reader meets the constants, and the document the comment points at must still exist.
/// </summary>
public class TunedConstantsBoundaryTests
{
    private const string Marker = "TUNED-CONSTANTS BOUNDARY (TD-118)";

    private static readonly string[] ClassFiles =
    [
        "src/Pine.Infrastructure/Ai/Vision/ReceiptDetector.cs",
        "src/Pine.Infrastructure/Ai/Vision/OpenCvImageProcessor.cs",
        "src/Pine.Infrastructure/Ai/Extraction/OnnxNlpFieldExtractor.cs",
        "src/Pine.Infrastructure/Ai/Vision/OnnxOcrEngine.cs",
        "src/Pine.Infrastructure/Ai/Vision/DbPostProcessor.cs",
        "src/Pine.Infrastructure/Ai/Vision/SkiaImagePreprocessor.cs",
        "src/Pine.Infrastructure/Ai/Vision/OnnxOcrOptions.cs",
        "src/Pine.Infrastructure/Ai/Vision/SkiaReceiptDetector.cs",
        "src/Pine.Infrastructure/Ai/Vision/ReceiptQuadFinder.cs",
        "src/Pine.Infrastructure/Ai/Vision/YoloPostProcessor.cs",
        "src/Pine.Infrastructure/Ai/Vision/YoloReceiptSegmenter.cs",
    ];

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pine.slnx")) && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Vision_class_file_states_the_boundary(string relative)
    {
        File.ReadAllText(Path.Combine(Root(), relative)).Should().Contain(Marker);
    }

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var f in ClassFiles) data.Add(f);
        return data;
    }

    [Fact]
    public void Architecture_doc_has_the_boundary_section_and_names_every_class()
    {
        var doc = File.ReadAllText(Path.Combine(Root(), "docs", "13-AI-Architecture.md"));

        doc.Should().Contain("Tuned-constants boundary (TD-118)").And.Contain(Marker);
        doc.Should().Contain("ReceiptDetector").And.Contain("OpenCvImageProcessor").And.Contain("HeuristicFieldExtractor");
        doc.Should().Contain("OnnxOcrEngine").And.Contain("DbPostProcessor").And.Contain("SkiaImagePreprocessor");
    }

    [Fact]
    public void Tuned_constants_are_not_exposed_through_PineOptions()
    {
        var options = File.ReadAllText(Path.Combine(Root(), "src", "Pine.Core", "Configuration", "PineOptions.cs"));

        options.Should().NotContain("AnchorCount").And.NotContain("LetterboxSize").And.NotContain("AspectRatioFloor");
    }
}
