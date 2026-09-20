using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Api;
using MantosExtract.Core.Auth;
using MantosExtract.Core.Extract;
using MantosExtract.Core.Recraft;

namespace MantosExtract.AddIn.Ui
{
    /// <summary>
    /// Remover fundo e vetorizar via Recraft (Dave, 2026-09-20). Mesmo desenho do refino
    /// (<c>MantosExtractBridge.Refine.cs</c>): o operador seleciona QUALQUER bitmap no Corel, clica
    /// no botão e a peça nova entra AO LADO do original, no mesmo tamanho físico — o original fica
    /// intacto e recuperável. Um arquivo à parte (partial) só pra não engordar o Bridge principal.
    ///
    /// Shell burro: lê a seleção, manda pro mantosfc (a Recraft nunca é chamada daqui) e coloca o
    /// resultado. A chave da Recraft é BYOK, guardada no mesmo cofre DPAPI da OpenAI. As duas
    /// operações são independentes; quem quer um vetor sem fundo remove o fundo e depois vetoriza a
    /// peça nova. Nenhuma mensagem de erro é fixa em português: o código vira chave i18n
    /// (<see cref="RecraftErrorMessages"/>), pra atender PT/ES/EN.
    /// </summary>
    public sealed partial class MantosExtractBridge
    {
        private const string RecraftSourceKey = "__recraft_source__";

        private readonly IRecraftClient _recraftClient = new RecraftClient();

        private async Task RunRecraftAsync(RecraftOperation operation, CancellationToken ct)
        {
            string name = RecraftOperations.LogName(operation);

            SessionState? session = _credentials.LoadSession();
            if (session == null) { PostAuth(AuthOrchestratorResult.ToLogin()); return; }

            string? recraftKey = _credentials.LoadRecraftKey();
            if (string.IsNullOrWhiteSpace(recraftKey))
            {
                PostRecraftDone(operation, false, "E_MISSING_RECRAFT_KEY", L("me.recraft.error.noKey"));
                return;
            }

            Post(new { type = "recraft", op = OperationTag(operation), stage = "running" });

            byte[] sourceBytes;
            try { sourceBytes = ReadSelectedBitmap(RecraftSourceKey); }
            catch (Exception ex)
            {
                // A mensagem da camada COM tem detalhe técnico (atributos do bitmap, HRESULT): vai
                // pro log; o operador recebe uma frase que diz o que fazer.
                MantosExtractLog.Write("Recraft " + name + ": leitura da seleção FALHOU: " + ex);
                PostRecraftDone(operation, false, "E_SELECTION", L("me.recraft.error.selection"));
                return;
            }

            ExtractedImage result;
            try
            {
                result = await _recraftClient
                    .RunAsync(operation, session.SessionId, recraftKey!, sourceBytes, "image/png", ct)
                    .ConfigureAwait(false);
            }
            catch (MantosExtractApiException ex)
            {
                MantosExtractLog.Write("Recraft " + name + " FAILED (" + ex.Code + "): " + ex.Message);
                if (ex.Code == "E_UNAUTHORIZED") { PostAuth(AuthOrchestratorResult.ToLogin()); return; }
                PostRecraftDone(operation, false, ex.Code, MessageFor(ex));
                return;
            }
            catch (OperationCanceledException)
            {
                MantosExtractLog.Write("Recraft " + name + " cancelado pelo operador");
                PostRecraftDone(operation, false, "E_CANCELLED", null);
                return;
            }
            catch (Exception ex)
            {
                MantosExtractLog.Write("Recraft " + name + " FAILED (inesperado): " + ex);
                PostRecraftDone(operation, false, "E_RECRAFT_FAILED", L("me.common.error.unknown"));
                return;
            }

            // Grava ANTES de mexer no Corel: se o Corel recusar, o resultado já gerado ainda existe.
            string savedPath;
            try { savedPath = SaveRecraftFile(operation, result.Bytes); }
            catch (Exception ex)
            {
                MantosExtractLog.Write("Recraft " + name + ": não consegui gravar o arquivo: " + ex);
                PostRecraftDone(operation, false, "E_RECRAFT_SAVE_FAILED", L("me.common.error.unknown"));
                return;
            }

            try
            {
                bool placed = _corel.PlaceBesideTrackedShape(RecraftSourceKey, savedPath,
                    RecraftOperations.NameSuffix(operation), RecraftOperations.ProducesVector(operation));
                if (!placed)
                {
                    PostRecraftDone(operation, false, "E_SOURCE_GONE", L("me.recraft.error.sourceGone"));
                    return;
                }
            }
            catch (Exception ex)
            {
                MantosExtractLog.Write("Recraft " + name + ": colocar no Corel FALHOU: " + ex);
                PostRecraftDone(operation, false, "E_IMPORT_FAILED", L("me.recraft.error.import"));
                return;
            }

            PostRecraftDone(operation, true, null, null);
        }

        /// <summary>Resultados ficam numa pasta própria dentro de Extractions: o histórico só lista
        /// pastas com batch.json, então ela não aparece lá (não é um lote de extração).</summary>
        private static string SaveRecraftFile(RecraftOperation operation, byte[] bytes)
        {
            string dir = Path.Combine(ExtractionsRootDir(), "recraft");
            Directory.CreateDirectory(dir);
            string extension = RecraftOperations.ProducesVector(operation) ? ".svg" : ".png";
            string path = Path.Combine(dir,
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + RecraftOperations.LogName(operation) + extension);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        /// <summary>Mensagem pro operador no idioma dele: pelo código, quando ele é da Recraft;
        /// senão o texto que veio do servidor.</summary>
        private string MessageFor(MantosExtractApiException ex)
        {
            string? key = RecraftErrorMessages.KeyFor(ex.Code);
            return key != null ? L(key) : ex.Message;
        }

        private static string OperationTag(RecraftOperation operation) =>
            operation == RecraftOperation.Vectorize ? "vectorize" : "removeBackground";

        private void PostRecraftDone(RecraftOperation operation, bool ok, string? code, string? error)
        {
            Post(new { type = "recraft", op = OperationTag(operation), stage = "done", ok, code, error });
        }

        /// <summary>Chave da Recraft (BYOK, Dave 2026-09-20), na tela de Configurações. Ao contrário
        /// da OpenAI, só responde "salva" se a chave realmente ficou guardada (o cofre lança se a
        /// gravação falhar).</summary>
        private void SaveRecraftKey(string key)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    _credentials.ClearRecraftKey();
                    Post(new { type = "recraftKey", ok = true, saved = false });
                    return;
                }

                _credentials.SaveRecraftKey(key);
                Post(new { type = "recraftKey", ok = true, saved = true });
            }
            catch (Exception ex)
            {
                // Nunca o valor da chave no log — só o motivo.
                MantosExtractLog.Write("SaveRecraftKey FAILED: " + ex.Message);
                Post(new { type = "recraftKey", ok = false, error = L("me.settings.recraft.saveFailed") });
            }
        }
    }
}
