using System;
using System.Collections.Generic;
using System.Text.Json;
using MantosExtract.Core.Detect;

namespace MantosExtract.Core.Extract
{
    /// <summary>
    /// Os OUTROS elementos da mesma detecção (Davidson, 2026-10-13 — camisa "Sou Patriota": a bandeira
    /// saía com o texto de cima, e o texto com pedaços do texto vizinho). Vão junto de cada extração,
    /// com a caixa, pra o servidor dizer à IA quais vizinhos já são peças próprias e em que faixa do
    /// recorte eles estão (só o nome não bastou quando as caixas se sobrepõem). É a lista da DETECÇÃO,
    /// não da seleção, e sem o próprio rótulo: duas "bandeira na manga" iguais não podem virar
    /// "deixe a bandeira na manga de fora" no pedido da própria bandeira.
    /// </summary>
    public static class OtherElements
    {
        public static IReadOnlyList<DetectedElement> For(IEnumerable<DetectedElement> all, DetectedElement current)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { (current.Label ?? "").Trim() };
            var result = new List<DetectedElement>();
            foreach (DetectedElement el in all)
            {
                if (el.Id == current.Id) continue;
                string label = (el.Label ?? "").Trim();
                if (label.Length == 0 || !seen.Add(label)) continue;
                result.Add(el);
            }
            return result;
        }

        /// <summary>Campo <c>other_elements</c> do /extract: <c>[{label, x_min, y_min, x_max, y_max}]</c>.</summary>
        public static string ToJson(IEnumerable<DetectedElement> others)
        {
            var items = new List<Dictionary<string, object>>();
            foreach (DetectedElement el in others)
            {
                items.Add(new Dictionary<string, object>
                {
                    ["label"] = (el.Label ?? "").Trim(),
                    ["x_min"] = el.Box.XMin,
                    ["y_min"] = el.Box.YMin,
                    ["x_max"] = el.Box.XMax,
                    ["y_max"] = el.Box.YMax,
                });
            }
            return JsonSerializer.Serialize(items);
        }
    }
}
