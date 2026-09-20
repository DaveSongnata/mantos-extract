using System.Collections.Generic;

namespace MantosExtract.Core.Layout
{
    /// <summary>
    /// Descobre QUAL shape um import criou, comparando os StaticIDs (IVGShape.StaticID, estável e
    /// único no documento) da camada antes e depois do import.
    ///
    /// Existe porque o jeito antigo — "o import deixa a peça nova selecionada, pega a seleção" —
    /// era uma suposição nunca provada, e falhou na máquina real do Dave (2026-09-13): o Corel não
    /// selecionou a peça importada, a seleção ainda era a peça anterior, e o Fundo passou a apontar
    /// pro shape do el_1. Ao fazer upscale do el_1 (que apaga o shape antigo), o Fundo "sumiu"
    /// pro addin, embora continuasse na página.
    /// </summary>
    public static class ImportedShapeResolver
    {
        /// <summary>Índice (na lista <paramref name="idsAfter"/>, 0-based) do shape cujo ID não
        /// existia antes, ou -1 se não houver exatamente um candidato claro. Quando o import cria
        /// mais de um shape novo (não deveria, pra PNG), prefere o de <paramref name="preferredIndex"/>
        /// se ele for novo, senão o primeiro novo — e nunca devolve um ID que já existia.</summary>
        public static int IndexOfNewShape(ICollection<int> idsBefore, IList<int> idsAfter, int preferredIndex = -1)
        {
            if (idsBefore == null || idsAfter == null) return -1;

            if (preferredIndex >= 0 && preferredIndex < idsAfter.Count && !idsBefore.Contains(idsAfter[preferredIndex]))
                return preferredIndex;

            for (int i = 0; i < idsAfter.Count; i++)
                if (!idsBefore.Contains(idsAfter[i])) return i;

            return -1;
        }

        /// <summary>Índices (0-based, na ordem da lista) de TODOS os shapes cujo ID não existia
        /// antes. Pra import de SVG, que o Corel pode devolver como vários objetos de topo — o
        /// chamador os agrupa numa peça só.</summary>
        public static List<int> IndicesOfNewShapes(ICollection<int> idsBefore, IList<int> idsAfter)
        {
            var result = new List<int>();
            if (idsBefore == null || idsAfter == null) return result;

            for (int i = 0; i < idsAfter.Count; i++)
                if (!idsBefore.Contains(idsAfter[i])) result.Add(i);

            return result;
        }
    }
}
