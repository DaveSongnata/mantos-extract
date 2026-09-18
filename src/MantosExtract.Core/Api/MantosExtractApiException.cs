using System;

namespace MantosExtract.Core.Api
{
    /// <summary>
    /// Thrown by <see cref="Detect.IDetectionClient"/> and <see cref="Extract.IExtractionClient"/>
    /// on any failure. Same shape as Auth.MantosfcAuthException — kept as a separate type
    /// because these calls carry a wider set of codes (E_NO_CREDITS, E_MISSING_OPENAI_KEY,
    /// E_INVALID_OPENAI_MODEL) that would misname an "Auth" exception.
    /// </summary>
    public sealed class MantosExtractApiException : Exception
    {
        public string Code { get; }

        public MantosExtractApiException(string code, string message) : base(message) { Code = code; }
        public MantosExtractApiException(string code, string message, Exception inner) : base(message, inner) { Code = code; }

        /// <summary>A conta OpenAI do CLIENTE (BYOK) ficou sem crédito ou bateu um teto de gasto —
        /// códigos que o mantosfc devolve ao reconhecer o erro da OpenAI (openai_quota_error.ts).
        /// Numa extração em lote isso vale pra todas as peças seguintes, então o lote para em vez
        /// de colher o mesmo erro peça por peça.</summary>
        public bool IsOpenAiQuotaExhausted => IsOpenAiQuotaCode(Code);

        public static bool IsOpenAiQuotaCode(string? code) =>
            code == "E_OPENAI_NO_CREDIT" || code == "E_OPENAI_SPEND_LIMIT";

        /// <summary>A chave OpenAI do cliente (BYOK) não tem acesso ao modelo de imagem novo
        /// (GPT Image 2.5). O addin oferece, num modal, usar o modelo anterior (Dave,
        /// 2026-09-18) — nunca troca sozinho, porque a qualidade muda.</summary>
        public bool IsOpenAiModelUnavailable => Code == "E_OPENAI_MODEL_UNAVAILABLE";

        /// <summary>Falha que se repetiria igual em todas as peças seguintes do lote — o lote
        /// para em vez de colher o mesmo erro peça por peça.</summary>
        public bool StopsBatch => IsOpenAiQuotaExhausted || IsOpenAiModelUnavailable;
    }
}
