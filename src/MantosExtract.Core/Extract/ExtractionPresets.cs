using System;

namespace MantosExtract.Core.Extract
{
    /// <summary>
    /// Os presets de qualidade que o operador escolhe no slider das Configurações (Dave,
    /// 2026-09-18) — o addin manda só o NOME e o mantosfc traduz em modelo/qualidade/tamanho
    /// (mantos_extract_presets.ts). Esta lista precisa bater com a do servidor: um valor fora dela
    /// volta como 422 E_INVALID_EXTRACT_PRESET.
    /// </summary>
    public static class ExtractionPresets
    {
        /// <summary>Do mais barato ao mais caro — a ordem é a do slider.</summary>
        public static readonly string[] All = { "low", "medium", "high", "xhigh", "max" };

        /// <summary>Igual ao comportamento anterior à migração para o GPT Image 2.5.</summary>
        public const string Default = "medium";

        public static bool IsValid(string? value) =>
            value != null && Array.IndexOf(All, value) >= 0;

        /// <summary>Nunca devolve algo que o servidor rejeitaria: valor desconhecido, vazio ou
        /// de um arquivo corrompido vira o padrão.</summary>
        public static string Normalize(string? value) => IsValid(value) ? value! : Default;
    }
}
