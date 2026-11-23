namespace Pine.Core.Ai;

public struct BoundingBox
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    public float Top => Y;
    public float Left => X;
    public float Right => X + Width;
    public float Bottom => Y + Height;
}

public struct OcrWord
{
    public string Text { get; set; }
    public float Confidence { get; set; }
    public BoundingBox BoundingBox { get; set; }

    /// <summary>
    /// Direction of the text line in degrees (0 level, positive turning clockwise down the page), from the detector's rotated box;
    /// 0 for engines that do not report it. Lets a caller level a tilted receipt.
    /// </summary>
    public float AngleDegrees { get; set; }
}
