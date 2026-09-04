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
    }
}
