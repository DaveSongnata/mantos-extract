using System.Linq;

namespace MantosExtract.Core.Extract
{
    public enum RefineSlot
    {
        /// <summary>A imagem que vai ser alterada — o resultado entra ao lado dela.</summary>
        Target,

        /// <summary>Imagem opcional só de consulta (ex.: a foto de onde a peça saiu).</summary>
        Reference,
    }

    /// <summary>
    /// As duas vagas do modal de refino (Dave, 2026-09-22, "opção A"): o operador escolhe
    /// explicitamente a imagem a alterar e, se quiser, uma referência, cada uma com o botão
    /// "Usar seleção". Explícito de propósito: a ordem da seleção múltipla do Corel não é
    /// confiável pra saber qual é qual, e a foto original costuma ser apagada do documento depois
    /// da extração, então nada aqui depende de achá-la sozinho.
    /// </summary>
    public sealed class RefineSelection
    {
        public const string NoTargetCode = "E_REFINE_NO_TARGET";
        public const string SameImageCode = "E_REFINE_SAME_IMAGE";

        public byte[]? Target { get; private set; }
        public byte[]? Reference { get; private set; }

        public static bool TryParseSlot(string? raw, out RefineSlot slot)
        {
            switch (raw)
            {
                case "target": slot = RefineSlot.Target; return true;
                case "reference": slot = RefineSlot.Reference; return true;
                default: slot = RefineSlot.Target; return false;
            }
        }

        /// <summary>Guarda <paramref name="png"/> na vaga. Devolve null se deu certo, ou
        /// <see cref="SameImageCode"/> (sem mexer na vaga) quando é a mesma imagem da outra vaga
        /// — mandar a mesma imagem como referência dela mesma não ajuda o modelo em nada e quase
        /// sempre é seleção esquecida no Corel.</summary>
        public string? Set(RefineSlot slot, byte[] png)
        {
            byte[]? other = slot == RefineSlot.Target ? Reference : Target;
            if (other != null && other.SequenceEqual(png)) return SameImageCode;

            if (slot == RefineSlot.Target) Target = png;
            else Reference = png;
            return null;
        }

        public void Clear(RefineSlot slot)
        {
            if (slot == RefineSlot.Target) Target = null;
            else Reference = null;
        }

        public void Reset()
        {
            Target = null;
            Reference = null;
        }

        /// <summary>Null se dá pra refinar; senão o código do que falta.</summary>
        public string? ValidateForRun() => Target == null ? NoTargetCode : null;
    }
}
