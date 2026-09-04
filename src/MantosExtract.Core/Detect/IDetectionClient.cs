using System.Threading;
using System.Threading.Tasks;

namespace MantosExtract.Core.Detect
{
    public interface IDetectionClient
    {
        Task<DetectionResult> DetectAsync(string sessionId, string openAiApiKey,
            byte[] imageBytes, string mimeType, CancellationToken ct);
    }
}
