using System;

namespace MantosExtract.Core.Recraft
{
    /// <summary>
    /// Código de erro -> chave i18n da mensagem pro operador. As mensagens do servidor são fixas em
    /// pt-BR, então pra atender PT/ES/EN o addin traduz pelo CÓDIGO (investigado em 2026-09-19:
    /// `BuildError` deixa passar o texto do servidor tal como veio). Código que não é da Recraft
    /// devolve null, e o chamador mostra a mensagem que veio do servidor.
    /// </summary>
    public static class RecraftErrorMessages
    {
        public static readonly string[] KnownCodes =
        {
            "E_MISSING_RECRAFT_KEY", "E_RECRAFT_INVALID_KEY", "E_RECRAFT_NO_CREDIT", "E_RECRAFT_RATE_LIMIT",
            "E_RECRAFT_UNAVAILABLE", "E_RECRAFT_BAD_RESPONSE", "E_RECRAFT_TIMEOUT", "E_RECRAFT_BAD_INPUT",
            "E_NETWORK", "E_DOWNLOAD_FAILED",
        };

        public static string? KeyFor(string? code)
        {
            switch (code)
            {
                case "E_MISSING_RECRAFT_KEY": return "me.recraft.error.noKey";
                case "E_RECRAFT_INVALID_KEY": return "me.recraft.error.invalidKey";
                case "E_RECRAFT_NO_CREDIT": return "me.recraft.error.noCredit";
                case "E_RECRAFT_RATE_LIMIT": return "me.recraft.error.rateLimit";
                case "E_RECRAFT_UNAVAILABLE":
                case "E_RECRAFT_BAD_RESPONSE": return "me.recraft.error.unavailable";
                case "E_RECRAFT_TIMEOUT": return "me.recraft.error.timeout";
                case "E_RECRAFT_BAD_INPUT": return "me.recraft.error.badInput";
                case "E_NETWORK": return "me.recraft.error.network";
                case "E_DOWNLOAD_FAILED": return "me.recraft.error.download";
                default: return null;
            }
        }
    }
}