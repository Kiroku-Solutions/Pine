namespace Pine.Core.Ai;

/// <summary>
/// The stages a receipt scan passes through, reported so a user interface can say which one is
/// running.
/// </summary>
/// <remarks>
/// <para>
/// AN ENUM RATHER THAN A STRING, because the application layer must not choose the words a user
/// reads. The words are a presentation concern -- they need translating (`41-Localization`), they
/// need to match the tone of the screen they appear on, and the same stage is worded differently in
/// a live camera view than in a file-upload view. What the application layer legitimately knows is
/// WHICH STAGE it is in, and that is all this type carries.
/// </para>
/// <para>
/// WHY THE STAGES ARE REPORTED AT ALL. Scanning a full-page receipt runs two neural networks and
/// takes several seconds on a normal machine. A single undifferentiated spinner for that whole
/// period reads as a hang; naming the stage is the difference between "it is stuck" and "it is
/// working". The stages are deliberately coarse -- there are only two -- because a progress report
/// nobody can act on is decoration, and pretending to finer granularity than the pipeline actually
/// has would be inventing precision.
/// </para>
/// </remarks>
public enum ScanReceiptStage
{
    /// <summary>Locating the receipt in the image and deciding whether there is one at all.</summary>
    DetectingReceipt = 0,

    /// <summary>Reading the text and pulling the merchant, date, total and line items out of it.</summary>
    ExtractingText = 1,
}
