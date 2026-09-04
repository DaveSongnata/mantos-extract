namespace MantosExtract.Core.Extract
{
    public sealed class ExtractedImage
    {
        public byte[] Bytes { get; }
        public string MimeType { get; }
        public string SourceUrl { get; }

        public ExtractedImage(byte[] bytes, string mimeType, string sourceUrl)
        {
            Bytes = bytes;
            MimeType = mimeType;
            SourceUrl = sourceUrl;
        }
    }
}
