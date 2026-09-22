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
    /// Refino por prompt livre (Dave, 2026-09-18): o operador escolhe QUALQUER bitmap do Corel —
    /// uma peça recém extraída ou um elemento qualquer do canvas —, descreve a alteração e recebe
    /// a versão ajustada AO LADO do original (que fica intacto e recuperável). Um arquivo à parte
    /// (partial) só pra não engordar mais o MantosExtractBridge.cs.
    ///
    /// Duas vagas desde 2026-09-22 (Dave, "opção A"): "imagem a alterar" e "referência
    /// (opcional)", cada uma preenchida pelo botão "Usar seleção" do modal. A referência é o caso
    /// "ajeite a posição do Zeus como na foto original": vai pro servidor como imagem só de
    /// consulta. Abrir o modal zera as duas vagas e já preenche a imagem a alterar com a seleção
    /// atual, se houver — o fluxo de sempre (selecionar, abrir, escrever, refinar) continua igual.
    ///
    /// Shell burro, como o resto do bridge: lê a seleção, manda pro mantosfc e coloca o resultado.
    /// Modelo, preset, tamanho e prompt são decisão do servidor; o preset é o mesmo do slider das
    /// Configurações. Não passa por upscale sozinho: a peça nova é um bitmap qualquer e o botão de
    /// upscale da seleção continua valendo pra ela.
    /// </summary>
    public sealed partial class MantosExtractBridge
    {
        private const string RefineSourceKey = "__refine_source__";
        private const string RefineReferenceKey = "__refine_reference__";
        // A seleção é lida com esta chave e só vira RefineSourceKey depois que a vaga aceita:
        // uma leitura recusada nunca pode mudar AO LADO DE QUEM o resultado vai entrar.
        private const string RefinePendingKey = "__refine_pending__";
        private const string RefinedNameSuffix = " (refinado)";

        private readonly RefineSelection _refineSelection = new RefineSelection();
        private string? _refineTargetThumb;
        private string? _refineReferenceThumb;

        /// <summary>Modal aberto: zera as vagas (nunca herdar a referência de outro trabalho) e
        /// preenche a imagem a alterar com a seleção atual, se ela for um bitmap.</summary>
        private void RefineOpen()
        {
            _refineSelection.Reset();
            _refineTargetThumb = ReplaceThumb(_refineTargetThumb, null);
            _refineReferenceThumb = ReplaceThumb(_refineReferenceThumb, null);

            string? error = null;
            if (_corel.SelectionIsSingleBitmap) error = CaptureRefineSlot(RefineSlot.Target);
            // Sem seleção ao abrir não é erro: a vaga só fica vazia esperando o "Usar seleção".
            PostRefineSlots(error);
        }

        private void RefineCapture(string slotRaw)
        {
            if (!RefineSelection.TryParseSlot(slotRaw, out RefineSlot slot))
            {
                MantosExtractLog.Write("Refino: vaga desconhecida '" + slotRaw + "'");
                return;
            }
            string? error = _corel.SelectionIsSingleBitmap
                ? CaptureRefineSlot(slot)
                : L("me.refine.slot.noSelection");
            PostRefineSlots(error);
        }

        private void RefineClear(string slotRaw)
        {
            if (!RefineSelection.TryParseSlot(slotRaw, out RefineSlot slot)) return;
            _refineSelection.Clear(slot);
            if (slot == RefineSlot.Target) _refineTargetThumb = ReplaceThumb(_refineTargetThumb, null);
            else _refineReferenceThumb = ReplaceThumb(_refineReferenceThumb, null);
            PostRefineSlots(null);
        }

        /// <summary>Lê a seleção pra vaga. Devolve null se deu certo, ou a mensagem pro operador.</summary>
        private string? CaptureRefineSlot(RefineSlot slot)
        {
            byte[] png;
            try { png = ReadSelectedBitmap(RefinePendingKey); }
            catch (Exception ex)
            {
                // Detalhe técnico da camada COM vai pro log; o operador recebe o que fazer.
                MantosExtractLog.Write("Refino: leitura da seleção (" + slot + ") FALHOU: " + ex);
                return L("me.refine.error.selection");
            }

            string? code = _refineSelection.Set(slot, png);
            if (code == RefineSelection.SameImageCode) return L("me.refine.error.sameImage");

            string thumb;
            try { thumb = WriteRefineThumb(png); }
            catch (Exception ex)
            {
                // Sem miniatura a vaga continua valendo; a tela só não mostra a imagem.
                MantosExtractLog.Write("Refino: miniatura não gravada: " + ex);
                thumb = "";
            }

            if (slot == RefineSlot.Target)
            {
                _corel.CopyTracking(RefinePendingKey, RefineSourceKey);
                _refineTargetThumb = ReplaceThumb(_refineTargetThumb, thumb);
            }
            else
            {
                _corel.CopyTracking(RefinePendingKey, RefineReferenceKey);
                _refineReferenceThumb = ReplaceThumb(_refineReferenceThumb, thumb);
            }
            MantosExtractLog.Write("Refino: vaga " + slot + " preenchida (" + png.Length + " bytes)");
            return null;
        }

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

            if (_refineSelection.ValidateForRun() is string missing)
            {
                PostRefineDone(false, missing, L("me.refine.error.noTarget"));
                return;
            }
            byte[] sourceBytes = _refineSelection.Target!;
            byte[]? referenceBytes = _refineSelection.Reference;

            Post(new { type = "refine", stage = "running" });
            MantosExtractLog.Write("Refino: imagem " + sourceBytes.Length + " bytes, referência " +
                (referenceBytes == null ? "nenhuma" : referenceBytes.Length + " bytes"));

            ExtractedImage refined;
            try
            {
                refined = await _extractionClient
                    .RefineAsync(session.SessionId, openAiKey!, ExtractionQualityStore.Read(), sourceBytes,
                                 "image/png", instruction, referenceBytes, ct, _useLegacyImageModel)
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
        /// caminho do upscale da seleção) e passa a rastreá-lo sob <paramref name="trackKey"/>.
        /// Lança se a seleção não for exatamente um bitmap.</summary>
        private byte[] ReadSelectedBitmap(string trackKey)
        {
            string sourcePath = Path.Combine(WorkDir(), "refino-origem-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                MantosExtractLog.Write("[refino] seleção lida: " + _corel.SaveSelectedBitmapForUpscale(sourcePath, trackKey));
                return File.ReadAllBytes(sourcePath);
            }
            finally
            {
                try { File.Delete(sourcePath); } catch { /* arquivo de trabalho em %TEMP% */ }
            }
        }

        /// <summary>Miniatura da vaga, servida à página por https://mantosextract.assets/ (nunca
        /// base64 pelo ExecuteScriptAsync — uma foto de celular tem vários MB).</summary>
        private static string WriteRefineThumb(byte[] png)
        {
            string name = "refine_" + Guid.NewGuid().ToString("N") + ".png";
            File.WriteAllBytes(Path.Combine(AssetsDir(), name), png);
            return name;
        }

        /// <summary>Apaga a miniatura antiga da vaga e devolve a nova (ou null).</summary>
        private static string? ReplaceThumb(string? oldName, string? newName)
        {
            if (!string.IsNullOrEmpty(oldName) && oldName != newName)
            {
                try { File.Delete(Path.Combine(AssetsDir(), oldName)); } catch { /* %TEMP% */ }
            }
            return newName;
        }

        private static object? ThumbSlot(byte[]? bytes, string? thumb) =>
            bytes == null ? null : new { url = string.IsNullOrEmpty(thumb) ? "" : "https://mantosextract.assets/" + thumb };

        private void PostRefineSlots(string? error)
        {
            Post(new
            {
                type = "refineSlots",
                target = ThumbSlot(_refineSelection.Target, _refineTargetThumb),
                reference = ThumbSlot(_refineSelection.Reference, _refineReferenceThumb),
                error,
            });
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
