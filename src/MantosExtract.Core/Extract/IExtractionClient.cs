using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Detect;

namespace MantosExtract.Core.Extract
{
    public interface IExtractionClient
    {
        Task<ExtractedImage> ExtractAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, BoundingBox box, string label, CancellationToken ct);

        /// <summary>"Fundo" (Dave, 2026-09-11) — método SEPARADO de ExtractAsync, de propósito:
        /// sem BoundingBox/label (a foto INTEIRA vai pro servidor, não uma região confirmada),
        /// endpoint dedicado /mantos-extract/extract-background. Nunca reusar ExtractAsync com
        /// um bbox fake para isso.</summary>
        Task<ExtractedImage> ExtractBackgroundAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, CancellationToken ct);
    }
}
