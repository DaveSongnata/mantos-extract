namespace MantosExtract.Core.Detect
{
    /// <summary>
    /// Normalized 0-1000 bounding box (docs/mantos-extract-spec.md §7.4) — stable across input
    /// images of different pixel sizes, the format OpenAI's own docs recommend for this kind of
    /// coordinate. This is a SUGGESTION for the overlay only (M1, CLAUDE.md) — extraction
    /// re-sends it to the server, which re-derives pixel coordinates against the real image.
    /// </summary>
    public sealed class BoundingBox
    {
        public int XMin { get; }
        public int YMin { get; }
        public int XMax { get; }
        public int YMax { get; }

        public BoundingBox(int xMin, int yMin, int xMax, int yMax)
        {
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        public bool IsDegenerate => XMax <= XMin || YMax <= YMin;
    }
}
