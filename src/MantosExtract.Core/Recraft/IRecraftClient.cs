using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Extract;

namespace MantosExtract.Core.Recraft
{
    public interface IRecraftClient
    {
        /// <summary>Remove o fundo OU vetoriza <paramref name="imageBytes"/> pelo mantosfc (nunca
        /// direto na Recraft). Devolve o PNG (com alpha) ou o SVG já baixado.</summary>
        Task<ExtractedImage> RunAsync(RecraftOperation operation, string sessionId, string recraftApiKey,
            byte[] imageBytes, string mimeType, CancellationToken ct);
    }
}