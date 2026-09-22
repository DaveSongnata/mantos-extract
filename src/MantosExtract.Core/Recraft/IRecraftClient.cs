using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Extract;

namespace MantosExtract.Core.Recraft
{
    public interface IRecraftClient
    {
        /// <summary>Remove o fundo OU vetoriza <paramref name="imageBytes"/> pelo mantosfc (nunca
        /// direto na Recraft). Devolve o PNG (com alpha) ou o SVG já baixado.
        ///
        /// Sem chave da Recraft na assinatura desde 2026-09-22: ela virou chave única da plataforma,
        /// no .env do mantosfc. O addin manda só a sessão — quem pode chamar, e quantas vezes, é o
        /// servidor que decide (cota por plano).</summary>
        Task<ExtractedImage> RunAsync(RecraftOperation operation, string sessionId,
            byte[] imageBytes, string mimeType, CancellationToken ct);
    }
}