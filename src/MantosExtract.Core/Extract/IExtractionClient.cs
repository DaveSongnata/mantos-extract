using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Detect;

namespace MantosExtract.Core.Extract
{
    public interface IExtractionClient
    {
        Task<ExtractedImage> ExtractAsync(string sessionId, string openAiApiKey,
            byte[] imageBytes, string mimeType, BoundingBox box, string label, CancellationToken ct);
    }
}
