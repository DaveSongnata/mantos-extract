using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MantosExtract.Core.Extract
{
    /// <summary>One element inside an <see cref="ExtractionBatchManifest"/> — mutable,
    /// plain-data shape (a JSON DTO, not a domain object with invariants to protect).</summary>
    public sealed class ExtractionBatchManifestElement
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";

        /// <summary>Null when the element was never written to disk (failed/skipped).</summary>
        public string? FileName { get; set; }

        public bool Ok { get; set; }
    }

    /// <summary>
    /// Filesystem-as-database (Dave, 2026-09-07): one of these lives as <c>batch.json</c> inside
    /// each extraction batch folder (see <see cref="ExtractionBatchNaming"/>) — the WHOLE
    /// history screen is just "enumerate folders, deserialize each manifest", no database
    /// anywhere. Always written, even for a batch that stopped early (E_NO_CREDITS mid-loop) —
    /// it reflects whatever actually happened, partial or complete.
    /// </summary>
    public sealed class ExtractionBatchManifest
    {
        public Guid BatchId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public int Total { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public int SkippedNoCredits { get; set; }
        public List<ExtractionBatchManifestElement> Elements { get; set; } = new List<ExtractionBatchManifestElement>();

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        public static ExtractionBatchManifest FromJson(string json) =>
            JsonSerializer.Deserialize<ExtractionBatchManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("Manifesto vazio ou inválido.");
    }
}
