using System.Collections.Generic;

namespace MantosExtract.Core.I18n
{
    internal static partial class Strings
    {
        public static Dictionary<string, string> Es() => new Dictionary<string, string>
        {
            ["me.app.title"] = "Mantos Extract",

            ["me.login.email.placeholder"] = "Correo electrónico",
            ["me.login.password.placeholder"] = "Contraseña",
            ["me.login.password.show"] = "Ver",
            ["me.login.password.hide"] = "Ocultar",
            ["me.login.submit"] = "Entrar",
            ["me.login.submitting"] = "Entrando...",
            ["me.login.error.generic"] = "No fue posible entrar. Intente de nuevo.",
            ["me.login.error.emailRequired"] = "Ingrese su correo electrónico.",
            ["me.login.error.passwordRequired"] = "Ingrese su contraseña.",

            ["me.home.credits.label"] = "{0} créditos",
            ["me.home.credits.one"] = "1 crédito",
            ["me.home.credits.zero"] = "Sin créditos",
            ["me.home.credits.unit"] = "créd.",
            ["me.home.settings.button"] = "Configuración",
            ["me.home.empty.title"] = "Próximamente",
            ["me.home.empty.body"] = "La extracción de elementos llega en la próxima etapa de este proyecto.",
            ["me.home.warning.dismiss"] = "Entendido",

            ["me.settings.title"] = "Configuración",
            ["me.settings.back"] = "Volver",
            ["me.settings.language.label"] = "Idioma",
            ["me.settings.openai.label"] = "Clave de OpenAI",
            ["me.settings.openai.placeholder"] = "sk-...",
            ["me.settings.openai.help"] = "Su confección usa su propia clave de OpenAI para detectar y extraer las imágenes. Péguela aquí una sola vez.",
            ["me.settings.openai.save"] = "Guardar clave",
            ["me.settings.openai.saved"] = "Clave guardada.",
            ["me.settings.openai.empty"] = "Aún no hay ninguna clave guardada.",
            ["me.settings.logout"] = "Salir",
            ["me.settings.logout.confirm"] = "¿Salir de su cuenta en esta computadora?",

            ["me.common.retry"] = "Intentar de nuevo",
            ["me.common.loading"] = "Cargando...",
            ["me.common.error.unknown"] = "Algo salió mal. Intente de nuevo.",
            ["me.common.details"] = "ver detalles técnicos",
            ["me.table.element"] = "Elemento",
            ["me.table.status"] = "Estado",

            ["me.detect.button"] = "Detectar",
            ["me.detect.emptyState.title"] = "Seleccione una imagen",
            ["me.detect.emptyState.body"] = "Haga clic en una imagen de su documento y luego aquí abajo.",
            ["me.detect.ready.title"] = "Imagen seleccionada",
            ["me.detect.error.noKey"] = "Configure su clave de OpenAI en Configuración antes de detectar.",
            ["me.detect.error.noSelection"] = "Seleccione una imagen en el documento antes de detectar.",
            ["me.detect.error.exportFailed"] = "No pude exportar la imagen seleccionada desde CorelDRAW.",
            ["me.detect.progress.exporting"] = "Exportando imagen...",
            ["me.detect.progress.detecting"] = "Detectando elementos...",

            ["me.select.title"] = "Elementos detectados",
            ["me.select.none"] = "No se detectó ningún elemento en esta imagen.",
            ["me.select.extractButton"] = "Extraer ({0})",
            ["me.select.selectedCount"] = "{0} seleccionados",
            ["me.select.creditNote"] = "Ya se usó 1 crédito en la detección. Extraer costará {0} más.",
            ["me.select.backButton"] = "Seleccionar otra imagen",

            ["me.extract.progress.extracting"] = "extrayendo...",
            ["me.extract.progress.upscaling"] = "mejorando resolución...",
            ["me.extract.progress.queued"] = "en espera",
            ["me.extract.progress.done"] = "listo",
            ["me.extract.progress.failed"] = "falló",
            ["me.extract.title"] = "Extrayendo elementos",
            ["me.extract.result.title"] = "Elementos extraídos",
            ["me.extract.result.summary"] = "{0} de {1} elementos extraídos con éxito",
            ["me.extract.result.newImage"] = "Extraer otra imagen",
            ["me.extract.result.skippedNoCredits"] = "{0} elemento(s) no se extrajeron por falta de crédito.",

            ["me.error.network.title"] = "No pudimos conectar",
            ["me.error.network.body"] = "Verifique su internet e intente de nuevo.",

            ["me.credits.zero.title"] = "Sus créditos se acabaron",
            ["me.credits.zero.body"] = "Hable con su confección para liberar más créditos.",
            ["me.credits.zero.contact"] = "Pida al responsable de su confección que renueve el plan en el panel de mantosfc.",
        };
    }
}
