using System;
using System.Collections.Generic;
using System.Text.Json;
using MantosExtract.Core.Api;

namespace MantosExtract.Core.Detect
{
    /// <summary>
    /// Parses the body of <c>POST /api/v1/mantos-extract/detect</c> — <c>{elements:[{id,label,
    /// bbox:{x_min,y_min,x_max,y_max}}]}</c> per docs/mantos-extract-spec.md §7.4.
    /// </summary>
    public sealed class DetectionResult
    {
        public IReadOnlyList<DetectedElement> Elements { get; }

        private DetectionResult(IReadOnlyList<DetectedElement> elements)
        {
            Elements = elements;
        }

        public static DetectionResult Parse(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                var elements = new List<DetectedElement>();
                if (root.TryGetProperty("elements", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in arr.EnumerateArray())
                    {
                        string id = Str(item, "id");
                        string label = Str(item, "label");
                        if (!item.TryGetProperty("bbox", out JsonElement bboxEl) || bboxEl.ValueKind != JsonValueKind.Object)
                            continue; // malformed element: skip it, never surface a half-parsed box

                        var box = new BoundingBox(Int(bboxEl, "x_min"), Int(bboxEl, "y_min"),
                                                   Int(bboxEl, "x_max"), Int(bboxEl, "y_max"));
                        if (box.IsDegenerate) continue;

                        elements.Add(new DetectedElement(id, label, box));
                    }
                }

                return new DetectionResult(elements);
            }
            catch (MantosExtractApiException) { throw; }
            catch (Exception ex)
            {
                throw new MantosExtractApiException("E_MALFORMED_RESPONSE",
                    "O servidor respondeu de um jeito inesperado. Tente novamente em instantes.", ex);
            }
        }

        private static string Str(JsonElement root, string name) =>
            root.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? (e.GetString() ?? "") : "";

        private static int Int(JsonElement root, string name) =>
            root.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;
    }
}
