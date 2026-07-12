namespace Pine.Infrastructure.Ai.Vision;

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
}
