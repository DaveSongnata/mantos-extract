using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Api;
using MantosExtract.Core.Auth;
using MantosExtract.Core.Extract;
using MantosExtract.Windows;

namespace MantosExtract.AddIn.Ui
{
    /// <summary>
    /// Refino por prompt livre (Dave, 2026-09-18): o operador seleciona QUALQUER bitmap no Corel —
    /// uma peça recém extraída ou um elemento qualquer do canvas —, descreve a alteração e recebe
    /// a versão ajustada AO LADO do original (que fica intacto e recuperável). Um arquivo à parte
    /// (partial) só pra não engordar mais o MantosExtractBridge.cs.
    ///
    /// Shell burro, como o resto do bridge: lê a seleção, manda pro mantosfc e coloca o resultado.
    /// Modelo, preset, tamanho e prompt são decisão do servidor; o preset é o mesmo do slider das
    /// Configurações. Não passa por upscale sozinho: a peça nova é um bitmap qualquer e o botão de
    /// upscale da seleção continua valendo pra ela.
    /// </summary>
    public sealed partial class MantosExtractBridge
    {
        private const string RefineSourceKey = "__refine_source__";
        private const string RefinedNameSuffix = " (refinado)";

        private async Task RunRefineAsync(string instruction, CancellationToken ct)
        {
            SessionState? session = _credentials.LoadSession();
            if (session == null) { PostAuth(AuthOrchestratorResult.ToLogin()); return; }

            string? openAiKey = _credentials.LoadOpenAiKey();
            if (string.IsNullOrWhiteSpace(openAiKey))
            {
                PostRefineDone(false, "E_MISSING_OPENAI_KEY", L("me.detect.error.noKey"));
                return;
            }

            instruction = (instruction ?? "").Trim();
            if (instruction.Length < 3)
            {
                PostRefineDone(false, "E_INVALID_INSTRUCTION", L("me.refine.error.tooShort"));
                return;
            }

            Post(new { type = "refine", stage = "running" });

            byte[] sourceBytes;
            try { sourceBytes = ReadSelectedBitmap(); }
            catch (Exception ex)
            {
                // A mensagem da camada COM tem detalhe técnico (atributos do bitmap, HRESULT): vai
                // pro log; o operador recebe uma frase que diz o que fazer.
                MantosExtractLog.Write("Refino: leitura da seleção FALHOU: " + ex);
                PostRefineDone(false, "E_SELECTION", L("me.refine.error.selection"));
                return;
            }

            ExtractedImage refined;
            try
            {
                refined = await _extractionClient
                    .RefineAsync(session.SessionId, openAiKey!, ExtractionQualityStore.Read(), sourceBytes,
                                 "image/png", instruction, ct, _useLegacyImageModel)
                    .ConfigureAwait(false);
            }
            catch (MantosExtractApiException ex)
            {
                MantosExtractLog.Write("Refino FAILED (" + ex.Code + "): " + ex.Message);
                if (ex.Code == "E_UNAUTHORIZED") { PostAuth(AuthOrchestratorResult.ToLogin()); return; }
                PostRefineDone(false, ex.Code, ex.Message);
                return;
            }
            catch (OperationCanceledException)
            {
                MantosExtractLog.Write("Refino cancelado pelo operador");
                PostRefineDone(false, "E_CANCELLED", null);
                return;
            }
            catch (Exception ex)
            {
                MantosExtractLog.Write("Refino FAILED (inesperado): " + ex);
                PostRefineDone(false, "E_REFINE_FAILED", L("me.common.error.unknown"));
                return;
            }

            // Grava ANTES de mexer no Corel: se o Corel recusar, a imagem já paga ainda existe.
            string savedPath;
            try { savedPath = SaveRefinedFile(refined.Bytes); }
            catch (Exception ex)
            {
                MantosExtractLog.Write("Refino: não consegui gravar o arquivo: " + ex);
                PostRefineDone(false, "E_REFINE_SAVE_FAILED", L("me.common.error.unknown"));
                return;
            }

            try
            {
                bool placed = _corel.PlaceBesideTrackedShape(RefineSourceKey, savedPath, RefinedNameSuffix);
                if (!placed)
                {
                    PostRefineDone(false, "E_SOURCE_GONE", L("me.refine.error.sourceGone"));
                    return;
                }
            }
            catch (Exception ex)
            {
                MantosExtractLog.Write("Refino: colocar no Corel FALHOU: " + ex);
                PostRefineDone(false, "E_IMPORT_FAILED", L("me.refine.error.import"));
                return;
            }

            PostRefineDone(true, null, null);
        }

        /// <summary>Grava o bitmap selecionado em resolução NATIVA e com transparência (mesmo
        /// caminho do upscale da seleção) e passa a rastreá-lo, pra o resultado entrar ao lado
        /// dele. Lança se a seleção não for exatamente um bitmap.</summary>
        private byte[] ReadSelectedBitmap()
        {
            string sourcePath = Path.Combine(WorkDir(), "refino-origem-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                MantosExtractLog.Write("[refino] seleção lida: " + _corel.SaveSelectedBitmapForUpscale(sourcePath, RefineSourceKey));
                return File.ReadAllBytes(sourcePath);
            }
            finally
            {
                try { File.Delete(sourcePath); } catch { /* arquivo de trabalho em %TEMP% */ }
            }
        }

        /// <summary>Refinos ficam numa pasta própria dentro de Extractions: o histórico só lista
        /// pastas com batch.json, então ela não aparece lá (não é um lote de extração).</summary>
        private static string SaveRefinedFile(byte[] bytes)
        {
            string dir = Path.Combine(ExtractionsRootDir(), "refinos");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_refino.png");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private void PostRefineDone(bool ok, string? code, string? error)
        {
            Post(new { type = "refine", stage = "done", ok, code, error });
        }
    }
}
