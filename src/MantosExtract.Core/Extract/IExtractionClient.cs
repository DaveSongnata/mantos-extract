using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Detect;

namespace MantosExtract.Core.Extract
{
    public interface IExtractionClient
    {
        /// <summary><paramref name="straightAngle"/> é a caixinha "ângulo reto" da tela de seleção
        /// (Davidson, 2026-10-13): true endireita a peça, false pede pra manter o ângulo da foto,
        /// null não manda o campo (o servidor usa o prompt de antes da caixinha).
        /// <paramref name="otherLabels"/> são os rótulos dos OUTROS elementos detectados
        /// (<see cref="OtherElementLabels"/>): a IA deixa de fora o vizinho que já é peça própria.</summary>
        Task<ExtractedImage> ExtractAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, BoundingBox box, string label, CancellationToken ct,
            bool legacyModel = false, bool? straightAngle = null, IReadOnlyList<string>? otherLabels = null);

        /// <summary>"Fundo" (Dave, 2026-09-11) — método SEPARADO de ExtractAsync, de propósito:
        /// sem BoundingBox/label (a foto INTEIRA vai pro servidor, não uma região confirmada),
        /// endpoint dedicado /mantos-extract/extract-background. Nunca reusar ExtractAsync com
        /// um bbox fake para isso.</summary>
        Task<ExtractedImage> ExtractBackgroundAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, CancellationToken ct, bool legacyModel = false);

        /// <summary>Refino por prompt livre (Dave, 2026-09-18) — endpoint dedicado
        /// /mantos-extract/refine: QUALQUER bitmap (não só uma extração) + uma instrução em texto
        /// livre; devolve a imagem alterada. O bitmap vai inteiro, sem redução.
        /// <paramref name="referenceBytes"/> (opcional, Dave 2026-09-22) é uma segunda imagem só
        /// de consulta — ex.: a foto de onde a peça saiu — que o operador escolheu no modal.
        /// <paramref name="straightAngle"/> é a caixinha "ângulo reto" (Dave, 2026-10-12): o
        /// servidor endireita a peça pra vista frontal; o texto do prompt é dele, não do cliente.</summary>
        Task<ExtractedImage> RefineAsync(string sessionId, string openAiApiKey, string quality,
            byte[] imageBytes, string mimeType, string instruction, byte[]? referenceBytes, CancellationToken ct,
            bool legacyModel = false, bool straightAngle = false);
    }
}