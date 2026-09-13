using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MantosExtract.Core.Upscale
{
    public enum UpscaleStatus
    {
        /// <summary>Output produced and sanity-checked (file exists). Note this is the model's
        /// NATIVE scale (4x), not the 2x the product ships — halving happens downstream
        /// (ApplyUpscale), because only the native scale tiles correctly.</summary>
        Success,
        /// <summary>realesrgan-ncnn-vulkan.exe is not installed on this machine — packaging step
        /// pending (plans/Phase_4.md); the caller degrades to the un-upscaled original.</summary>
        BinaryMissing,
        /// <summary>Ran past the timeout and was killed — never left hanging (M5).</summary>
        Timeout,
        /// <summary>Não existe GPU com Vulkan utilizável nesta máquina (<c>vkCreateInstance
        /// failed</c> / <c>invalid gpu device</c>). É categoria à parte de <see cref="Failed"/>
        /// porque não é erro do arquivo nem coisa que "tentar de novo" resolva: o Real-ESRGAN
        /// NCNN-Vulkan não tem caminho de CPU (M5/CLAUDE.md), então nesta máquina o upscale
        /// simplesmente não existe e o operador tem que ser avisado disso, não de um erro.</summary>
        GpuUnavailable,
        /// <summary>Exited non-zero, or exited 0 without producing the expected output file.</summary>
        Failed,
    }

    /// <summary>Onde a rede roda. Sempre <see cref="Gpu"/> por padrão; <see cref="Cpu"/> só
    /// quando o operador pede explicitamente, porque é ~10× mais lento (89s contra 9s, medido).</summary>
    public enum UpscaleDevice
    {
        Gpu,
        /// <summary>Vulkan por SOFTWARE (lavapipe) + modelo compacto. Roda em máquina sem placa
        /// de vídeo nenhuma — VM incluída.</summary>
        Cpu,
    }

    public sealed class UpscaleResult
    {
        public UpscaleStatus Status { get; }
        public string? OutputPath { get; }

        /// <summary>Escala que o arquivo em <see cref="OutputPath"/> realmente tem. O modo GPU
        /// devolve a escala nativa do modelo (4×) e o chamador reduz pela metade; o modo CPU usa
        /// um modelo com escala 2 nativa e já devolve o 2× final. Quem chama tem que olhar isto
        /// em vez de assumir — reduzir um 2× pela metade entregaria 1×.</summary>
        public int ScaleApplied { get; }

        /// <summary>O que o binário disse antes de falhar (exit code + últimas linhas de
        /// stdout/stderr) + o inventário da pasta de modelos. Existe porque um "Failed" sozinho no
        /// docker.log não dá pra diagnosticar nada (Dave, 2026-09-11 — upscale falhou na máquina
        /// dele em 2,7s e o log não tinha UMA linha dizendo por quê). Nulo quando deu certo.</summary>
        public string? Diagnostics { get; }

        private UpscaleResult(UpscaleStatus status, string? outputPath, string? diagnostics = null, int scaleApplied = 0)
        {
            Status = status;
            OutputPath = outputPath;
            Diagnostics = diagnostics;
            ScaleApplied = scaleApplied;
        }

        public static UpscaleResult Ok(string outputPath, int scaleApplied) =>
            new UpscaleResult(UpscaleStatus.Success, outputPath, null, scaleApplied);
        public static UpscaleResult Of(UpscaleStatus status, string? diagnostics = null) =>
            new UpscaleResult(status, null, diagnostics);
    }

    /// <summary>
    /// Invokes the pre-built <c>realesrgan-ncnn-vulkan.exe</c> as an external process — same
    /// pattern as SisCut.Engine.EngineRunner (write input → Process with hard timeout → check
    /// exit code → read output file), NOT the Ponte de Ação file-polling protocol (CHANGELOG.md
    /// explains why that precedent didn't fit: it is peer-app→SisCut, backwards from a shim
    /// invoking its own child process). Fixed 2x factor (M8) — no operator-facing option.
    /// </summary>
    public sealed class UpscaleRunner
    {
        private readonly UpscalePaths _paths;

        public UpscaleRunner(UpscalePaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        /// <summary>Upscales <paramref name="inputPngPath"/> at the model's native scale (4x).
        /// The caller halves it to the 2x the product actually ships (M8) — see ApplyUpscale.
        /// Never throws for an expected failure mode (missing binary, timeout, bad exit) — those
        /// are all just a <see cref="UpscaleStatus"/> the caller degrades on (Fase 4: import the
        /// original PNG instead of blocking the whole element).</summary>
        public UpscaleResult Run(string inputPngPath, int timeoutSeconds = 90,
                                 UpscaleDevice device = UpscaleDevice.Gpu)
        {
            // Modelo ausente conta como BinaryMissing: é instalação incompleta do mesmo jeito, e
            // deixar seguir só troca uma mensagem clara por um crash do processo filho.
            if (!_paths.IsUsable)
                return UpscaleResult.Of(UpscaleStatus.BinaryMissing, DescribeFailure("(não chegou a rodar)", ""));

            if (device == UpscaleDevice.Cpu && !_paths.HasCpuFallback)
                return UpscaleResult.Of(UpscaleStatus.BinaryMissing,
                    "modo CPU pedido, mas o Vulkan por software não está instalado: icd=" +
                    (File.Exists(_paths.CpuIcdPath) ? "ok" : "AUSENTE"));

            Directory.CreateDirectory(_paths.TempDir);
            int scale = device == UpscaleDevice.Cpu ? UpscalePaths.CpuNativeScale : UpscalePaths.NativeScale;
            string outputPath = Path.Combine(_paths.TempDir,
                Path.GetFileNameWithoutExtension(inputPngPath) + "_native" + scale + "x.png");

            // No modo CPU não há tile automático que salve nem GPU pra faltar memória: uma
            // tentativa só, com o modelo compacto, e o que falhar falhou.
            if (device == UpscaleDevice.Cpu)
            {
                PrepareCpuIcd();
                UpscaleStatus cpuStatus = RunOnce(inputPngPath, outputPath, AutoTile, timeoutSeconds,
                                                  out string cpuOutput, device);
                return cpuStatus == UpscaleStatus.Success
                    ? UpscaleResult.Ok(outputPath, scale)
                    : UpscaleResult.Of(cpuStatus,
                        DescribeFailure(DescribeAttempt(AutoTile, UpscaleDevice.Cpu), cpuOutput));
            }

            // Primeira tentativa: tile automático (o binário dimensiona pela VRAM que ele acha que
            // tem). É o mais rápido quando dá certo.
            UpscaleStatus status = RunOnce(inputPngPath, outputPath, AutoTile, timeoutSeconds, out string output);
            if (status == UpscaleStatus.Success) return UpscaleResult.Ok(outputPath, scale);

            // Sem Vulkan não adianta tentar de novo com tile menor: o processo nem chega a alocar
            // nada, morre em vkCreateInstance.
            if (IndicatesNoUsableGpu(output))
                return UpscaleResult.Of(UpscaleStatus.GpuUnavailable, DescribeFailure(DescribeAttempt(AutoTile), output));

            // Segunda tentativa, com tile pequeno e explícito. O tile automático é escolhido a
            // partir da memória de vídeo REPORTADA, que numa VM ou GPU integrada não corresponde
            // ao que dá pra alocar de verdade: o processo inicializa o Vulkan normalmente e só
            // morre lá adiante, sem imprimir nada (exit=-1, ~3s — assinatura real da máquina do
            // Dave, 2026-09-11). Tile menor = fatias menores na VRAM, mais lento porém viável.
            // Só faz sentido depois de um Failed: num Timeout, insistir só gastaria o dobro.
            if (status != UpscaleStatus.Failed)
                return UpscaleResult.Of(status, DescribeFailure(DescribeAttempt(AutoTile), output));

            UpscaleStatus retry = RunOnce(inputPngPath, outputPath, FallbackTile, timeoutSeconds, out string retryOutput);
            if (retry == UpscaleStatus.Success) return UpscaleResult.Ok(outputPath, scale);

            // A 1ª tentativa pode falhar calada e só a 2ª revelar que o problema é Vulkan (foi o
            // que aconteceu na máquina do Dave, 2026-09-11), então a checagem vale pras duas.
            if (retry == UpscaleStatus.Failed && IndicatesNoUsableGpu(retryOutput))
                return UpscaleResult.Of(UpscaleStatus.GpuUnavailable,
                    DescribeFailure(DescribeAttempt(AutoTile), output) + " | 2a tentativa: " + retryOutput);

            return UpscaleResult.Of(retry,
                DescribeFailure(DescribeAttempt(AutoTile), output) +
                " | 2a tentativa (-t " + FallbackTile + "): " + retryOutput);
        }

        /// <summary>
        /// Reconhece, na saída do binário, "esta máquina não tem GPU com Vulkan utilizável".
        /// <c>vkCreateInstance failed -9</c> é <c>VK_ERROR_INCOMPATIBLE_DRIVER</c>: o loader do
        /// Vulkan (que vem no Windows) não achou NENHUM driver de GPU que o implemente — típico de
        /// VM sem aceleração 3D. Não é falta de arquivo nosso: o ICD do Vulkan vem com o driver da
        /// placa de vídeo, não com este addon.
        /// </summary>
        internal static bool IndicatesNoUsableGpu(string processOutput)
        {
            if (string.IsNullOrEmpty(processOutput)) return false;
            return processOutput.IndexOf("vkCreateInstance failed", StringComparison.OrdinalIgnoreCase) >= 0
                || processOutput.IndexOf("invalid gpu device", StringComparison.OrdinalIgnoreCase) >= 0
                || processOutput.IndexOf("no vulkan device", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Reescreve o manifesto do ICD com o caminho ABSOLUTO da DLL, num arquivo nosso em
        /// TempDir, e passa esse arquivo pro loader.
        ///
        /// O ICD que vem do Mesa traz <c>"library_path": ".\\vulkan_lvp.dll"</c> — relativo. A
        /// especificação diz que isso se resolve a partir da pasta do manifesto, mas o loader
        /// que vale é o que está instalado no Windows da máquina do cliente, não o nosso: se
        /// ele resolver a partir do diretório de trabalho (que fixamos na pasta do .exe, um
        /// nível acima de cpu-vulkan/), a DLL não é encontrada, o ICD é descartado em silêncio e
        /// o erro que sobra é o mesmo "vkCreateInstance failed / invalid gpu device" de quando
        /// não há GPU — indistinguível (caso real na VM do Dave, 2026-09-12). Caminho absoluto
        /// não tem essa ambiguidade.
        /// </summary>
        private void PrepareCpuIcd()
        {
            try
            {
                string dll = Path.Combine(_paths.CpuVulkanDir, "vulkan_lvp.dll");
                if (!File.Exists(dll)) return;

                string json =
                    "{\n" +
                    "    \"ICD\": {\n" +
                    "        \"api_version\": \"1.2.0\",\n" +
                    "        \"library_arch\": \"64\",\n" +
                    "        \"library_path\": \"" + dll.Replace("\\", "\\\\") + "\"\n" +
                    "    },\n" +
                    "    \"file_format_version\": \"1.0.0\"\n" +
                    "}\n";

                Directory.CreateDirectory(_paths.TempDir);
                File.WriteAllText(_paths.EffectiveCpuIcdPath, json);
            }
            catch
            {
                // Se não deu pra escrever, RunOnce ainda tenta com o ICD original do Mesa.
            }
        }

        /// <summary>Tile automático: o binário decide pelo que enxerga de VRAM.</summary>
        private const int AutoTile = 0;

        /// <summary>Tile do retry. 128 é conservador de propósito — o objetivo aqui é CABER numa
        /// GPU modesta, não ser rápido; quem tem folga já terminou na primeira tentativa.</summary>
        private const int FallbackTile = 128;

        private UpscaleStatus RunOnce(string inputPngPath, string outputPath, int tileSize,
                                      int timeoutSeconds, out string processOutput,
                                      UpscaleDevice device = UpscaleDevice.Gpu)
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);

            bool cpu = device == UpscaleDevice.Cpu;
            string arguments = BuildArguments(inputPngPath, outputPath, _paths.ModelsDir,
                cpu ? UpscalePaths.CpuModelName : UpscalePaths.ModelName,
                cpu ? UpscalePaths.CpuNativeScale : UpscalePaths.NativeScale, tileSize);

            // Roda COM O CWD na pasta do próprio binário. O Real-ESRGAN resolve o caminho do
            // modelo de forma peculiar (com um -m que ele não acha, a mensagem de erro sai com o
            // diretório do exe concatenado na frente do caminho absoluto), e dentro do CorelDRAW
            // o CWD herdado é o do host, não o nosso — fixar isso tira uma variável inteira da
            // equação em vez de torcer pro comportamento ser o mesmo nas duas máquinas.
            // No modo CPU, o ICD do lavapipe é apontado por variável de ambiente SÓ neste
            // processo filho: é assim que o loader do Vulkan escolhe um driver por software em
            // vez de procurar GPU. VK_DRIVER_FILES é o nome atual e VK_ICD_FILENAMES o antigo —
            // manda os dois porque a versão do loader é a do Windows da máquina, não a nossa.
            string? icd = null;
            string exe = _paths.ExecutablePath;
            if (cpu)
            {
                icd = File.Exists(_paths.EffectiveCpuIcdPath) ? _paths.EffectiveCpuIcdPath : _paths.CpuIcdPath;
                // Modo CPU roda a CÓPIA do exe que fica em cpu-vulkan/, ao lado do nosso próprio
                // vulkan-1.dll (loader Khronos 1.4.x). O Windows resolve DLL primeiro na pasta do
                // exe, então o loader do sistema — que numa VM é antigo ou stub e descarta o
                // lavapipe moderno com "vkCreateInstance failed -9", mesmo com DLL íntegra e ICD
                // absoluto (medido na VM do Dave, 2026-09-13) — deixa de ser usado. O modo GPU
                // segue com o loader do sistema, que é o que casa com o driver da placa.
                if (File.Exists(_paths.CpuExecutablePath)) exe = _paths.CpuExecutablePath;
            }

            return RunProcess(exe, arguments, outputPath, timeoutSeconds,
                out processOutput, Path.GetDirectoryName(exe), icd);
        }

        private string DescribeAttempt(int tileSize, UpscaleDevice device = UpscaleDevice.Gpu)
        {
            bool cpu = device == UpscaleDevice.Cpu;
            string args = BuildArguments("<entrada>", "<saida>", _paths.ModelsDir,
                cpu ? UpscalePaths.CpuModelName : UpscalePaths.ModelName,
                cpu ? UpscalePaths.CpuNativeScale : UpscalePaths.NativeScale, tileSize);
            return cpu ? args + " [CPU: VK_DRIVER_FILES=" + _paths.CpuIcdPath + "]" : args;
        }

        /// <summary>Junta tudo que ajuda a explicar uma falha numa linha só de log: os argumentos
        /// reais, o que o processo imprimiu, e se os arquivos que ele precisa estão mesmo no
        /// disco — "modelo ausente", "sem GPU Vulkan" e "caminho errado" são causas distintas que
        /// produzem o mesmo <see cref="UpscaleStatus.Failed"/> e só se separam por isto.</summary>
        private string DescribeFailure(string arguments, string processOutput)
        {
            var sb = new StringBuilder();
            sb.Append("args=[").Append(arguments).Append("]");

            sb.Append(" exe=").Append(_paths.ExecutableExists ? "ok" : "AUSENTE");

            string modelBin = Path.Combine(_paths.ModelsDir, UpscalePaths.ModelName + ".bin");
            string modelParam = Path.Combine(_paths.ModelsDir, UpscalePaths.ModelName + ".param");
            sb.Append(" modelo.bin=").Append(File.Exists(modelBin) ? "ok" : "AUSENTE");
            sb.Append(" modelo.param=").Append(File.Exists(modelParam) ? "ok" : "AUSENTE");

            try
            {
                sb.Append(" modelsDir=[");
                if (Directory.Exists(_paths.ModelsDir))
                {
                    string[] files = Directory.GetFiles(_paths.ModelsDir);
                    for (int i = 0; i < files.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(Path.GetFileName(files[i]));
                    }
                }
                else sb.Append("PASTA NAO EXISTE");
                sb.Append("]");
            }
            catch (Exception ex) { sb.Append("erro ao listar: ").Append(ex.Message).Append("]"); }

            // Estado da pasta do Vulkan-em-software COM TAMANHO: File.Exists devolve true pra um
            // arquivo truncado/zerado, e uma DLL de 54MB extraída pela metade falha do mesmo
            // jeito que uma ausente (vkCreateInstance failed), sem nada no log que distinga.
            try
            {
                sb.Append(" cpuVulkan=[");
                if (Directory.Exists(_paths.CpuVulkanDir))
                {
                    string[] files = Directory.GetFiles(_paths.CpuVulkanDir);
                    for (int i = 0; i < files.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(Path.GetFileName(files[i])).Append(':');
                        try { sb.Append(new FileInfo(files[i]).Length); } catch { sb.Append('?'); }
                    }
                    if (files.Length == 0) sb.Append("PASTA VAZIA");
                }
                else sb.Append("PASTA NAO EXISTE");
                sb.Append(" | icd usado=")
                  .Append(File.Exists(_paths.EffectiveCpuIcdPath) ? "reescrito(absoluto)" : "original(relativo)");
                sb.Append(']');
            }
            catch (Exception ex) { sb.Append("erro: ").Append(ex.Message).Append(']'); }

            if (!string.IsNullOrWhiteSpace(processOutput))
                sb.Append(" saida=[").Append(processOutput).Append("]");

            return sb.ToString();
        }

        /// <summary>Pure — the exact CLI shape realesrgan-ncnn-vulkan.exe expects (-i in, -o
        /// out, -s scale, -m models dir, -n model name). Kept separate from process invocation so
        /// it is unit-testable without spawning anything.
        ///
        /// Two flags that look optional and are NOT (both learned the hard way, 2026-09-11):
        /// <c>-m</c>/<c>-n</c>, because without them the binary silently uses its own default
        /// model (<c>realesr-animevideov3</c>), which zeroes the alpha channel; and
        /// <paramref name="scale"/>, which MUST be the model's native scale
        /// (<see cref="UpscalePaths.NativeScale"/>) — asking a native-4x model for <c>-s 2</c>
        /// returns a tile mosaic, not a 2x image.</summary>
        public static string BuildArguments(string inputPath, string outputPath, string modelsDir,
                                            string modelName, int scale, int tileSize = 0)
        {
            // -v: o binário só fala quando é verboso. Sem isso, uma falha vem com stderr VAZIO e
            // não há como saber se foi GPU, memória ou modelo (medido na máquina do Dave:
            // "exit=-1 | (sem saida)"). Custa nada — o progresso é filtrado na captura, e nada é
            // logado quando dá certo.
            string args = $"-i {Quote(inputPath)} -o {Quote(outputPath)} -s {scale} " +
                          $"-m {Quote(modelsDir)} -n {Quote(modelName)} -v";
            // -t 0 é o default (automático) e o binário aceita, mas omitir deixa o comando mais
            // honesto sobre o que está sendo pedido de diferente numa segunda tentativa.
            if (tileSize > 0) args += $" -t {tileSize}";
            return args;
        }

        /// <summary>
        /// Generic external-process runner: start, wait up to <paramref name="timeoutSeconds"/>,
        /// kill on timeout, and check BOTH the exit code and that <paramref name="expectedOutputPath"/>
        /// actually exists — a 0 exit with no output file is still a failure, never a silent
        /// pass-through of the un-upscaled input.
        /// </summary>
        internal static UpscaleStatus RunProcess(string exePath, string arguments, string expectedOutputPath, int timeoutSeconds) =>
            RunProcess(exePath, arguments, expectedOutputPath, timeoutSeconds, out _);

        internal static UpscaleStatus RunProcess(string exePath, string arguments, string expectedOutputPath,
                                                 int timeoutSeconds, out string processOutput,
                                                 string? workingDirectory = null,
                                                 string? vulkanIcdPath = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? "",
                // Capturado (era descartado) pra uma falha do binário chegar no docker.log com o
                // motivo junto. Lido por EVENTO, nunca ReadToEnd depois do Wait: o Real-ESRGAN
                // despeja progresso sem parar e um pipe cheio travaria o processo pra sempre.
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            if (!string.IsNullOrEmpty(vulkanIcdPath))
            {
                psi.EnvironmentVariables["VK_DRIVER_FILES"] = vulkanIcdPath;   // loader atual
                psi.EnvironmentVariables["VK_ICD_FILENAMES"] = vulkanIcdPath;  // loader antigo
            }

            var captured = new List<string>();
            void Capture(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                // Linhas de progresso ("12.24%") são ruído: centenas por execução e nenhuma
                // ajuda a explicar uma falha.
                if (line!.TrimEnd().EndsWith("%", StringComparison.Ordinal)) return;
                lock (captured)
                {
                    captured.Add(line.Trim());
                    if (captured.Count > MaxCapturedLines) captured.RemoveAt(0);
                }
            }

            UpscaleStatus status;
            using (Process? proc = Process.Start(psi))
            {
                if (proc == null) { processOutput = "Process.Start devolveu null"; return UpscaleStatus.Failed; }

                proc.OutputDataReceived += (s, e) => Capture(e.Data);
                proc.ErrorDataReceived += (s, e) => Capture(e.Data);
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (!proc.WaitForExit(timeoutSeconds * 1000))
                {
                    try { proc.Kill(); } catch { /* already gone */ }
                    try { proc.WaitForExit(); } catch { /* best effort */ }
                    processOutput = Join(captured);
                    return UpscaleStatus.Timeout;
                }

                int exitCode = proc.ExitCode;
                status = exitCode != 0
                    ? UpscaleStatus.Failed
                    : (File.Exists(expectedOutputPath) ? UpscaleStatus.Success : UpscaleStatus.Failed);

                processOutput = status == UpscaleStatus.Success
                    ? ""
                    : "exit=" + exitCode + (exitCode == 0 ? " (saiu 0 mas nao gerou o arquivo)" : "") +
                      (captured.Count > 0 ? " | " + Join(captured) : " | (sem saida)");
            }
            return status;
        }

        private const int MaxCapturedLines = 12;

        private static string Join(List<string> lines)
        {
            lock (lines) return string.Join(" / ", lines.ToArray());
        }

        private static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOf(' ') < 0 && arg.IndexOf('"') < 0 && arg.IndexOf('\t') < 0)
                return arg;

            var sb = new StringBuilder();
            sb.Append('"');
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { backslashes++; }
                else if (c == '"') { sb.Append('\\', backslashes * 2 + 1); backslashes = 0; sb.Append('"'); }
                else { sb.Append('\\', backslashes); backslashes = 0; sb.Append(c); }
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
