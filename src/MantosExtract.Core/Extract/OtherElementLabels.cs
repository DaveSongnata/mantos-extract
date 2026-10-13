using System;
using System.Collections.Generic;
using MantosExtract.Core.Detect;

namespace MantosExtract.Core.Extract
{
    /// <summary>
    /// Rótulos dos OUTROS elementos da mesma detecção (Davidson, 2026-10-13 — camisa "Sou Patriota":
    /// a bandeira saía com o texto de cima, e o texto com pedaços do texto vizinho). Vai junto de cada
    /// extração pra o servidor dizer à IA quais vizinhos já são peças próprias. É a lista da DETECÇÃO,
    /// não da seleção, e sem o próprio rótulo: duas "bandeira na manga" iguais não podem virar "deixe
    /// a bandeira na manga de fora" no pedido da própria bandeira.
    /// </summary>
    public static class OtherElementLabels
    {
        public static IReadOnlyList<string> For(IEnumerable<DetectedElement> all, DetectedElement current)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { (current.Label ?? "").Trim() };
            var result = new List<string>();
            foreach (DetectedElement el in all)
            {
                if (el.Id == current.Id) continue;
                string label = (el.Label ?? "").Trim();
                if (label.Length == 0 || !seen.Add(label)) continue;
                result.Add(label);
            }
            return result;
        }
    }
}
