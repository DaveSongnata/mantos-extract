namespace MantosExtract.Core.Detect
{
    public sealed class DetectedElement
    {
        public string Id { get; }
        public string Label { get; }
        public BoundingBox Box { get; }

        /// <summary>Operator's checklist choice (mockup 4.4) — starts unchecked; the C# side
        /// never pre-selects anything (M1: detection is a suggestion, never a final cut).</summary>
        public bool Confirmed { get; set; }

        public DetectedElement(string id, string label, BoundingBox box)
        {
            Id = id;
            Label = label;
            Box = box;
        }
    }
}
