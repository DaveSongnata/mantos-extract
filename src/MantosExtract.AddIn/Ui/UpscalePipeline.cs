using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MantosExtract.Core.Upscale;
using MantosExtract.Interop;
using MantosExtract.Windows;

namespace MantosExtract.AddIn.Ui
{
    internal enum UpscaleOutcomeKind
    {
        /// <summary>Peça trocada no Corel pela versão 2×.</summary>
        Done,
        /// <summary>Sem GPU compatível e com o modo CPU instalado: a UI oferece "Upscale (CPU)"
        /// (fluxo pedido pelo Dave, 2026-09-12). Não é falha — é uma pergunta.</summary>
        OfferCpu,
        /// <summary>2× gerado e salvo na pasta, mas a peça já não está no documento.</summary>
        ShapeGone,
        /// <summary>2× gerado e salvo na pasta, mas o Corel recusou a troca.</summary>
        CorelFailed,
        /// <summary>A IA não conseguiu (ver log).</summary>
        Failed,
        /// <summary>O operador cancelou; o processo do upscale foi morto e nada foi trocado.</summary>
        Cancelled,
    }

    internal sealed class UpscaleOutcome
    {
        public UpscaleOutcomeKind Kind { get; }
        public string Method { get; }
        public UpscaleOutcome(UpscaleOutcomeKind kind, string method = "") { Kind = kind; Method = method; }
    }

    /// <summary>
    /// Upscale por IA de uma peça: (1) Real-ESRGAN na GPU; se a máquina não tiver GPU com
    /// Vulkan, a UI oferece (2) Real-ESRGAN compacto na CPU (lavapipe + loader Vulkan próprio).
    /// Só IA — nenhum fallback de interpolação, que não é upscale de verdade.
    ///
    /// "Babyproof": cada etapa é logada com início, duração e — se falhar — a exceção COMPLETA
    /// com stack trace, sob uma tag única por execução, mais um retrato do ambiente. Nenhuma
    /// exceção escapa daqui: falha vira um desfecho <see cref="UpscaleOutcomeKind.Failed"/>.
    /// </summary>
    internal sealed class UpscalePipeline
    {
        private const int GpuTimeoutSeconds = 300;
        private const int CpuTimeoutSeconds = 600;

        private readonly UpscalePaths _paths;
        private readonly UpscaleRunner _runner;
        private readonly ICorelHost _corel;
        private readonly string _workDir;
        private string _tag = "[upscale] ";

        public UpscalePipeline(UpscalePaths paths, UpscaleRunner runner, ICorelHost corel, string workDir)
        {
            _paths = paths;
            _runner = runner;
            _corel = corel;
            _workDir = workDir;
        }

        public UpscaleOutcome Run(string id, string finalPath, UpscaleDevice device, CancellationToken ct = default,
                                  bool renameShape = true)
        {
            _tag = "[upscale " + id + " #" + Guid.NewGuid().ToString("N").Substring(0, 6) + "] ";
            var total = Stopwatch.StartNew();
            var temps = new List<string>();
            try
            {
                Log("INICIO pedido=" + device + " arquivo=" + finalPath);
                LogEnvironment(finalPath);

                string baseName = Path.GetFileNameWithoutExtension(finalPath);
                string? twoX = null;
                string method = "";

                // ---- degraus de IA ----------------------------------------------------------
                if (device == UpscaleDevice.Gpu)
                {
                    if (!_paths.IsUsable)
                    {
                        Log("degrau 1 (IA GPU) PULADO: binário/modelo não instalados");
                        if (_paths.HasCpuFallback)
                        {
                            Log("modo CPU instalado -> oferecendo Upscale (CPU) ao operador");
                            return new UpscaleOutcome(UpscaleOutcomeKind.OfferCpu);
                        }
                    }
                    else
                    {
                        UpscaleResult? r = null;
                        Step("degrau 1: IA na GPU", () => r = _runner.Run(finalPath, GpuTimeoutSeconds, UpscaleDevice.Gpu, ct));
                        if (r != null)
                        {
                            Log("degrau 1 resultado: " + r.Status + Diag(r));
                            if (r.OutputPath != null) temps.Add(r.OutputPath);
                            if (r.Status == UpscaleStatus.Cancelled) return Cancelled();

                            if (r.Status == UpscaleStatus.Success)
                            {
                                twoX = FinishAiOutput(r, baseName, temps);
                                if (twoX != null) method = "ia-gpu";
                            }
                            else if (r.Status == UpscaleStatus.GpuUnavailable && _paths.HasCpuFallback)
                            {
                                Log("sem GPU compatível e modo CPU instalado -> oferecendo Upscale (CPU) ao operador");
                                return new UpscaleOutcome(UpscaleOutcomeKind.OfferCpu);
                            }
                        }
                    }
                }
                else
                {
                    if (!_paths.HasCpuFallback)
                    {
                        Log("degrau 2 (IA CPU) PULADO: Vulkan por software/modelo compacto não instalados");
                    }
                    else
                    {
                        twoX = RunAiOnCpu(finalPath, baseName, temps, ct);
                        if (twoX != null) method = "ia-cpu";
                    }
                }

                // Sem fallback de interpolação de propósito (Dave, 2026-09-13): ampliar por
                // Lanczos/bicúbico não é upscale — não cria detalhe — e entregar isso com o mesmo
                // selo "2x" enganaria o operador. Se a IA não rodou, o clique falha e diz.
                // Cancelado durante a IA ou logo depois: não troca nada no Corel nem no disco.
                if (ct.IsCancellationRequested) return Cancelled();

                if (twoX == null)
                {
                    Log("upscale por IA não foi possível — ver as exceções/diagnósticos acima");
                    return new UpscaleOutcome(UpscaleOutcomeKind.Failed);
                }

                Log("imagem 2× pronta via '" + method + "': " + twoX + SizeOf(twoX));

                // ---- entrega: pasta do lote + canvas ---------------------------------------
                // Grava na pasta ANTES de mexer no Corel: se o Corel recusar, o operador ainda
                // tem a versão em alta no disco.
                string delivered = twoX;
                Step("gravar versão 2× na pasta do lote", () => File.Copy(delivered, finalPath, overwrite: true));

                bool? replaced = null;
                Step("Corel: substituir a peça pela versão 2×", () => replaced = _corel.ReplaceTrackedShape(id, delivered));

                if (replaced == true)
                {
                    // Upscale avulso da seleção não renomeia: o nome original do operador já foi
                    // herdado em ReplaceTrackedShape.
                    if (renameShape) Step("Corel: renomear a peça", () => _corel.RenameLastImportedShape(baseName));
                    return new UpscaleOutcome(UpscaleOutcomeKind.Done, method);
                }
                if (replaced == false)
                {
                    Log("a peça não está mais no documento (apagada pelo operador); arquivo 2× ficou salvo na pasta");
                    return new UpscaleOutcome(UpscaleOutcomeKind.ShapeGone, method);
                }
                return new UpscaleOutcome(UpscaleOutcomeKind.CorelFailed, method);
            }
            catch (Exception ex)
            {
                // Não deveria acontecer (toda etapa já é protegida), mas é aqui que se garante.
                Log("FATAL fora das etapas: " + ex);
                return new UpscaleOutcome(UpscaleOutcomeKind.Failed);
            }
            finally
            {
                foreach (string t in temps) TryDelete(t);
                Log("FIM em " + total.ElapsedMilliseconds + "ms");
            }
        }

        /// <summary>O modo GPU devolve 4× (escala nativa do modelo) e precisa da metade; o de CPU já
        /// devolve 2×. Null se a redução falhar — a cadeia então desce pros degraus sem GPU.</summary>
        private string? FinishAiOutput(UpscaleResult r, string baseName, List<string> temps)
        {
            if (r.ScaleApplied <= UpscalePaths.CpuNativeScale) return r.OutputPath;

            string half = Path.Combine(_workDir, baseName + "_ia_half.png");
            temps.Add(half);
            return Step("reduzir 4× -> 2×", () => ImageDownscaler.Halve(r.OutputPath!, half)) ? half : null;
        }

        private UpscaleOutcome Cancelled()
        {
            Log("CANCELADO pelo operador — processo do upscale encerrado, nada foi trocado");
            return new UpscaleOutcome(UpscaleOutcomeKind.Cancelled);
        }

        private string? RunAiOnCpu(string finalPath, string baseName, List<string> temps, CancellationToken ct)
        {
            string rgb = Path.Combine(_workDir, baseName + "_rgb.png");
            string alpha = Path.Combine(_workDir, baseName + "_alpha.png");
            temps.Add(rgb);
            temps.Add(alpha);

            // O modelo compacto zera o alpha: separa antes, recombina depois.
            bool hasAlpha = false;
            if (!Step("degrau 2: separar alpha", () => hasAlpha = ImageAlphaSplitter.Split(finalPath, rgb, alpha)))
                return null;
            Log("imagem " + (hasAlpha ? "COM transparência (vai recombinar)" : "opaca (segue direto)"));

            UpscaleResult? r = null;
            string input = hasAlpha ? rgb : finalPath;
            Step("degrau 2: IA na CPU (lavapipe)", () => r = _runner.Run(input, CpuTimeoutSeconds, UpscaleDevice.Cpu, ct));
            if (r == null) return null;
            Log("degrau 2 resultado: " + r.Status + Diag(r));
            if (r.OutputPath != null) temps.Add(r.OutputPath);
            if (r.Status != UpscaleStatus.Success) return null;

            string? twoX = FinishAiOutput(r, baseName, temps);
            if (twoX == null || !hasAlpha) return twoX;

            string rgba = Path.Combine(_workDir, baseName + "_rgba2x.png");
            temps.Add(rgba);
            return Step("degrau 2: recombinar alpha", () => ImageAlphaSplitter.Combine(twoX, alpha, rgba)) ? rgba : null;
        }

        private bool Step(string name, Action action)
        {
            Log(">> " + name);
            var sw = Stopwatch.StartNew();
            try
            {
                action();
                Log("<< " + name + " OK (" + sw.ElapsedMilliseconds + "ms)");
                return true;
            }
            catch (Exception ex)
            {
                Log("!! " + name + " FALHOU (" + sw.ElapsedMilliseconds + "ms): " + ex);
                return false;
            }
        }

        /// <summary>Retrato da máquina no começo de cada execução: quando algo falhar num cliente,
        /// o log já traz o contexto que antes precisava de uma rodada extra de perguntas.</summary>
        private void LogEnvironment(string finalPath)
        {
            try
            {
                string cpuDir = _paths.CpuVulkanDir;
                Log("ambiente: os=" + Environment.OSVersion.VersionString +
                    " integridade=" + MediumIntegrityLauncher.DescribeCurrentIntegrity() +
                    " processo64=" + Environment.Is64BitProcess +
                    " cpus=" + Environment.ProcessorCount +
                    " memoriaProcessoMB=" + (Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)) +
                    " entrada=" + SizeOf(finalPath));
                Log("instalação: exe=" + Flag(_paths.ExecutablePath) +
                    " modeloGPU=" + Flag(_paths.ModelBinPath) +
                    " IsUsable=" + _paths.IsUsable +
                    " HasCpuFallback=" + _paths.HasCpuFallback +
                    " cpuVulkan=[" + ListDir(cpuDir) + "]" +
                    " loaderSistema=" + Flag(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll")) +
                    " workDir=" + _workDir);
            }
            catch (Exception ex)
            {
                Log("ambiente: não consegui coletar (" + ex.Message + ")");
            }
        }

        private static string Flag(string path)
        {
            try { return File.Exists(path) ? "ok(" + new FileInfo(path).Length + "b)" : "AUSENTE"; }
            catch (Exception ex) { return "ERRO(" + ex.Message + ")"; }
        }

        private static string SizeOf(string path)
        {
            try { return " (" + new FileInfo(path).Length + " bytes)"; }
            catch { return " (tamanho ilegível)"; }
        }

        private static string ListDir(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return "PASTA NAO EXISTE";
                var parts = new List<string>();
                foreach (string f in Directory.GetFiles(dir)) parts.Add(Path.GetFileName(f) + ":" + new FileInfo(f).Length);
                return parts.Count == 0 ? "VAZIA" : string.Join(", ", parts.ToArray());
            }
            catch (Exception ex) { return "ERRO(" + ex.Message + ")"; }
        }

        private static string Diag(UpscaleResult r) =>
            string.IsNullOrEmpty(r.Diagnostics) ? "" : " — " + r.Diagnostics;

        private void Log(string message) => MantosExtractLog.Write(_tag + message);

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
