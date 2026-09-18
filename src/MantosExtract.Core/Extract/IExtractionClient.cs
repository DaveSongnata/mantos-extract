using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Detect;

namespace MantosExtract.Core.Extract
{
    public interface IExtractionClient
    {
        Task<ExtractedImage> ExtractAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, BoundingBox box, string label, CancellationToken ct,
            bool legacyModel = false);

        /// <summary>"Fundo" (Dave, 2026-09-11) — método SEPARADO de ExtractAsync, de propósito:
        /// sem BoundingBox/label (a foto INTEIRA vai pro servidor, não uma região confirmada),
        /// endpoint dedicado /mantos-extract/extract-background. Nunca reusar ExtractAsync com
        /// um bbox fake para isso.</summary>
        Task<ExtractedImage> ExtractBackgroundAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, CancellationToken ct, bool legacyModel = false);

        /// <summary>Refino por prompt livre (Dave, 2026-09-18) — endpoint dedicado
        /// /mantos-extract/refine: QUALQUER bitmap (não só uma extração) + uma instrução em texto
        /// livre; devolve a imagem alterada. O bitmap vai inteiro, sem redução.</summary>
        Task<ExtractedImage> RefineAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, string instruction, CancellationToken ct,
            bool legacyModel = false);
    }
}