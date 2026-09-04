using System.Collections.Generic;

namespace MantosExtract.Core.I18n
{
    internal static partial class Strings
    {
        public static Dictionary<string, string> Pt() => new Dictionary<string, string>
        {
            ["me.app.title"] = "Mantos Extract",

            ["me.login.email.placeholder"] = "E-mail",
            ["me.login.password.placeholder"] = "Senha",
            ["me.login.password.show"] = "Ver",
            ["me.login.password.hide"] = "Ocultar",
            ["me.login.submit"] = "Entrar",
            ["me.login.submitting"] = "Entrando...",
            ["me.login.error.generic"] = "Não foi possível entrar. Tente novamente.",
            ["me.login.error.emailRequired"] = "Digite seu e-mail.",
            ["me.login.error.passwordRequired"] = "Digite sua senha.",

            ["me.home.credits.label"] = "{0} créditos",
            ["me.home.credits.one"] = "1 crédito",
            ["me.home.credits.zero"] = "Sem créditos",
            ["me.home.credits.unit"] = "créd.",
            ["me.home.settings.button"] = "Configurações",
            ["me.home.empty.title"] = "Em breve",
            ["me.home.empty.body"] = "A extração de elementos chega na próxima etapa deste projeto.",
            ["me.home.warning.dismiss"] = "Entendi",

            ["me.settings.title"] = "Configurações",
            ["me.settings.back"] = "Voltar",
            ["me.settings.language.label"] = "Idioma",
            ["me.settings.openai.label"] = "Chave da OpenAI",
            ["me.settings.openai.placeholder"] = "sk-...",
            ["me.settings.openai.help"] = "Sua confecção usa a própria chave da OpenAI para detectar e extrair as imagens. Cole aqui uma vez só.",
            ["me.settings.openai.save"] = "Salvar chave",
            ["me.settings.openai.saved"] = "Chave salva.",
            ["me.settings.openai.empty"] = "Nenhuma chave salva ainda.",
            ["me.settings.logout"] = "Sair",
            ["me.settings.logout.confirm"] = "Sair da sua conta neste computador?",

            ["me.common.retry"] = "Tentar de novo",
            ["me.common.loading"] = "Carregando...",
            ["me.common.error.unknown"] = "Algo deu errado. Tente novamente.",
            ["me.common.details"] = "ver detalhes técnicos",
            ["me.table.element"] = "Elemento",
            ["me.table.status"] = "Status",

            ["me.detect.button"] = "Detectar",
            ["me.detect.emptyState.title"] = "Selecione uma imagem",
            ["me.detect.emptyState.body"] = "Clique numa imagem no seu documento, depois clique aqui embaixo.",
            ["me.detect.ready.title"] = "Imagem selecionada",
            ["me.detect.error.noKey"] = "Configure sua chave da OpenAI em Configurações antes de detectar.",
            ["me.detect.error.noSelection"] = "Selecione uma imagem no documento antes de detectar.",
            ["me.detect.error.exportFailed"] = "Não consegui exportar a imagem selecionada do CorelDRAW.",
            ["me.detect.progress.exporting"] = "Exportando imagem...",
            ["me.detect.progress.detecting"] = "Detectando elementos...",

            ["me.select.title"] = "Elementos detectados",
            ["me.select.none"] = "Nenhum elemento detectado nesta imagem.",
            ["me.select.extractButton"] = "Extrair ({0})",
            ["me.select.selectedCount"] = "{0} selecionados",
            ["me.select.creditNote"] = "1 crédito já usado na detecção. Extrair vai custar mais {0}.",
            ["me.select.backButton"] = "Selecionar outra imagem",

            ["me.extract.progress.extracting"] = "extraindo...",
            ["me.extract.progress.upscaling"] = "melhorando resolução...",
            ["me.extract.progress.queued"] = "na fila",
            ["me.extract.progress.done"] = "pronto",
            ["me.extract.progress.failed"] = "falhou",
            ["me.extract.title"] = "Extraindo elementos",
            ["me.extract.result.title"] = "Elementos extraídos",
            ["me.extract.result.summary"] = "{0} de {1} elementos extraídos com sucesso",
            ["me.extract.result.newImage"] = "Extrair outra imagem",
            ["me.extract.result.skippedNoCredits"] = "{0} elemento(s) não foram extraídos por falta de crédito.",

            ["me.error.network.title"] = "Não conseguimos conectar",
            ["me.error.network.body"] = "Verifique sua internet e tente de novo.",

            ["me.credits.zero.title"] = "Seus créditos acabaram",
            ["me.credits.zero.body"] = "Fale com sua confecção para liberar mais créditos.",
            ["me.credits.zero.contact"] = "Peça ao responsável pela sua confecção para renovar o plano no painel do mantosfc.",
        };
    }
}
