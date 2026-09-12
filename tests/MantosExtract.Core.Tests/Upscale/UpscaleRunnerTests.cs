using System;
using System.IO;
using MantosExtract.Core.Upscale;
using Xunit;

namespace MantosExtract.Core.Tests.Upscale
{
    /// <summary>
    /// UpscaleRunner.Run itself is NOT tested end-to-end here — that would require the real
    /// realesrgan-ncnn-vulkan.exe binary, which is a packaging artifact this repo does not
    /// bundle (plans/Phase_4.md). What IS tested, against a REAL child process (cmd.exe, always
    /// present on Windows — same "test the real thing, don't fake Process" philosophy as
    /// SisCut.Engine.EngineRunnerTests, which runs the actual nest-engine binary): the exact CLI
    /// shape built for the real tool, and the timeout/exit-code/output-file plumbing that would
    /// behave identically no matter which executable sits behind it.
    /// </summary>
    public class UpscaleRunnerTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "MantosExtractTests_" + Guid.NewGuid().ToString("N"));

        public UpscaleRunnerTests() => Directory.CreateDirectory(_tempDir);
        public void Dispose() { try { Directory.Delete(_tempDir, recursive: true); } catch { } }

        [Fact]
        public void BuildArguments_ProducesExpectedCliShape()
        {
            string args = UpscaleRunner.BuildArguments(
                @"C:\temp\in.png", @"C:\temp\out.png", @"C:\temp\models", "realesrgan-x4plus-anime", 4);

            Assert.Equal(
                "-i C:\\temp\\in.png -o C:\\temp\\out.png -s 4 -m C:\\temp\\models -n realesrgan-x4plus-anime -v",
                args);
        }

        [Fact]
        public void BuildArguments_QuotesPathsWithSpaces()
        {
            string args = UpscaleRunner.BuildArguments(
                @"C:\Program Files\in.png", @"C:\Program Files\out.png", @"C:\Program Files\models",
                "realesrgan-x4plus-anime", 4);

            Assert.Equal(
                "-i \"C:\\Program Files\\in.png\" -o \"C:\\Program Files\\out.png\" -s 4 " +
                "-m \"C:\\Program Files\\models\" -n realesrgan-x4plus-anime -v",
                args);
        }

        [Fact]
        public void BuildArguments_EmitsTileOnlyWhenExplicit()
        {
            // O retry (GPU sem VRAM pro tile automático) é a ÚNICA coisa que muda entre as duas
            // tentativas — se -t vazasse pra primeira, ela ficaria lenta à toa pra todo mundo.
            string auto = UpscaleRunner.BuildArguments(@"C:\in.png", @"C:\out.png", @"C:\m", "modelo", 4);
            string tiled = UpscaleRunner.BuildArguments(@"C:\in.png", @"C:\out.png", @"C:\m", "modelo", 4, 128);

            Assert.DoesNotContain("-t", auto);
            Assert.EndsWith("-t 128", tiled);
        }

        [Theory]
        // Saída REAL da máquina do Dave (docker.log, 2026-09-11) — VM sem driver Vulkan.
        // -9 é VK_ERROR_INCOMPATIBLE_DRIVER: o loader do Vulkan não achou ICD de GPU nenhum.
        [InlineData("exit=-1 | vkCreateInstance failed -9 / vkCreateInstance failed -9 / invalid gpu device")]
        [InlineData("vkCreateInstance failed -9")]
        [InlineData("invalid gpu device")]
        [InlineData("no vulkan device")]
        public void IndicatesNoUsableGpu_RecognisesTheRealVulkanFailures(string output)
        {
            Assert.True(UpscaleRunner.IndicatesNoUsableGpu(output));
        }

        [Theory]
        [InlineData("")]
        [InlineData("exit=-1 | (sem saida)")]
        [InlineData("decode image C:\\temp\\x.png failed")]
        [InlineData("unknown model dir type")]
        public void IndicatesNoUsableGpu_DoesNotSwallowOtherFailures(string output)
        {
            // Estes têm causa e tratamento próprios — chamar tudo de "sem GPU" esconderia
            // exatamente os erros que o retry/diagnóstico conseguem resolver.
            Assert.False(UpscaleRunner.IndicatesNoUsableGpu(output));
        }

        [Fact]
        public void BuildArguments_AsksForVerbose_SoAFailureIsNeverSilent()
        {
            // Sem -v o binário falha com stderr VAZIO (medido em produção: "exit=-1 | (sem
            // saida)") e não dá pra distinguir GPU de memória de modelo.
            Assert.Contains("-v", UpscaleRunner.BuildArguments(@"C:\in.png", @"C:\out.png", @"C:\m", "modelo", 4));
        }

        [Fact]
        public void BuildArguments_UsesTheModelsNativeScale_NeverAHardcodedTwo()
        {
            // Regressão (2026-09-11): pedir -s 2 de um modelo nativo 4x devolve um MOSAICO de
            // tiles desencontrados, não um 2x. O 2x do produto (M8) vem do downscale posterior,
            // nunca desta flag — se alguém "consertar" isso pra 2 de novo, o upscale volta a sair
            // quebrado e só dá pra ver olhando a imagem.
            string args = UpscaleRunner.BuildArguments(
                @"C:\in.png", @"C:\out.png", @"C:\models", UpscalePaths.ModelName, UpscalePaths.NativeScale);

            Assert.Contains("-s 4", args);
            Assert.DoesNotContain("-s 2", args);
            Assert.Equal(4, UpscalePaths.NativeScale);
        }

        [Fact]
        public void ModelName_IsTheAlphaSafeOne_NeverTheBinarysDefault()
        {
            // realesr-animevideov3 é o default do binário e o mais rápido, mas ZERA o canal alpha
            // (testado: alphaMax=0 num PNG transparente) — todo elemento extraído é PNG com
            // transparência (M6), então ele sairia invisível no Corel.
            Assert.Equal("realesrgan-x4plus-anime", UpscalePaths.ModelName);
            Assert.DoesNotContain("animevideov3", UpscalePaths.ModelName);
        }

        [Fact]
        public void CpuModel_IsTheCompactOne_AndItsNativeScaleIsAlreadyTheShippedTwo()
        {
            // O modelo principal (RRDB) levou 640s numa peça em CPU pura; este (SRVGG compacto)
            // levou 89s na mesma imagem. Além disso tem escala 2 NATIVA, então o modo CPU não
            // passa pelo ImageDownscaler — reduzir um 2× pela metade entregaria 1×.
            Assert.Equal("realesr-animevideov3", UpscalePaths.CpuModelName);
            Assert.Equal(2, UpscalePaths.CpuNativeScale);
            Assert.NotEqual(UpscalePaths.ModelName, UpscalePaths.CpuModelName);
        }

        [Fact]
        public void HasCpuFallback_RequiresTheSoftwareVulkanAndTheCompactModel()
        {
            string exe = Path.Combine(_tempDir, UpscalePaths.InstalledExeName);
            File.WriteAllText(exe, "exe");
            var paths = new UpscalePaths(exe, _tempDir);

            Assert.False(paths.HasCpuFallback); // nada instalado ainda

            Directory.CreateDirectory(paths.CpuVulkanDir);
            File.WriteAllText(paths.CpuIcdPath, "{}");
            File.WriteAllText(Path.Combine(paths.CpuVulkanDir, "vulkan_lvp.dll"), "dll");
            Assert.False(paths.HasCpuFallback); // ICD sem o modelo compacto não serve

            Directory.CreateDirectory(paths.ModelsDir);
            string stem = Path.Combine(paths.ModelsDir,
                UpscalePaths.CpuModelName + "-x" + UpscalePaths.CpuNativeScale);
            File.WriteAllText(stem + ".param", "p");
            File.WriteAllText(stem + ".bin", "b");
            Assert.True(paths.HasCpuFallback);
        }

        [Fact]
        public void Run_CpuRequestedWithoutSoftwareVulkan_SaysSo_WithoutSpawning()
        {
            // Pedir CPU numa instalação que não trouxe o lavapipe tem que falhar explicando,
            // não subir o binário pra ele morrer em vkCreateInstance de novo.
            string exe = Path.Combine(_tempDir, UpscalePaths.InstalledExeName);
            File.WriteAllText(exe, "exe");
            Directory.CreateDirectory(Path.Combine(_tempDir, "models"));
            var paths = new UpscalePaths(exe, _tempDir);
            File.WriteAllText(Path.Combine(paths.ModelsDir, UpscalePaths.ModelName + ".param"), "p");
            File.WriteAllText(Path.Combine(paths.ModelsDir, UpscalePaths.ModelName + ".bin"), "b");

            UpscaleResult result = new UpscaleRunner(paths)
                .Run(Path.Combine(_tempDir, "in.png"), 30, UpscaleDevice.Cpu);

            Assert.Equal(UpscaleStatus.BinaryMissing, result.Status);
            Assert.Contains("Vulkan por software", result.Diagnostics);
        }

        [Fact]
        public void ModelsDir_SitsNextToTheExecutable()
        {
            var paths = new UpscalePaths(@"C:\Program Files\MantosExtract\upscale\realesrgan-ncnn-vulkan.exe");

            Assert.Equal(@"C:\Program Files\MantosExtract\upscale\models", paths.ModelsDir);
        }

        [Fact]
        public void RunProcess_ProcessProducesExpectedFile_ReturnsSuccess()
        {
            string src = Path.Combine(_tempDir, "src.txt");
            string dst = Path.Combine(_tempDir, "dst.txt");
            File.WriteAllText(src, "conteudo");

            UpscaleStatus status = UpscaleRunner.RunProcess(
                "cmd.exe", $"/c copy \"{src}\" \"{dst}\"", dst, timeoutSeconds: 15);

            Assert.Equal(UpscaleStatus.Success, status);
            Assert.True(File.Exists(dst));
        }

        [Fact]
        public void RunProcess_NonZeroExit_ReturnsFailed()
        {
            string dst = Path.Combine(_tempDir, "never-created.txt");

            UpscaleStatus status = UpscaleRunner.RunProcess("cmd.exe", "/c exit 1", dst, timeoutSeconds: 15);

            Assert.Equal(UpscaleStatus.Failed, status);
        }

        [Fact]
        public void RunProcess_ZeroExitButNoOutputFile_ReturnsFailed()
        {
            // A tool that reports success without writing the file it promised is still a
            // failure from our side — we never silently pass through a missing upscale.
            string dst = Path.Combine(_tempDir, "never-created.txt");

            UpscaleStatus status = UpscaleRunner.RunProcess("cmd.exe", "/c exit 0", dst, timeoutSeconds: 15);

            Assert.Equal(UpscaleStatus.Failed, status);
        }

        [Fact]
        public void RunProcess_ExceedsTimeout_KillsProcessAndReturnsTimeout()
        {
            string dst = Path.Combine(_tempDir, "never-created.txt");

            UpscaleStatus status = UpscaleRunner.RunProcess(
                "cmd.exe", "/c ping -n 30 127.0.0.1 >nul", dst, timeoutSeconds: 1);

            Assert.Equal(UpscaleStatus.Timeout, status);
        }

        [Fact]
        public void Run_ExePresentButModelMissing_IsBinaryMissing_NeverSpawnsTheCrashingProcess()
        {
            // Regressão (Dave, 2026-09-11): um instalador levou o .exe SEM a pasta models/. Como
            // só o .exe era checado, o botão de upscale aparecia, o processo subia e CRASHAVA
            // (exit 0xC0000409) depois de ~3s, sem explicação nenhuma no log.
            string exe = Path.Combine(_tempDir, UpscalePaths.InstalledExeName);
            File.WriteAllText(exe, "não é um exe de verdade, mas existe");
            var paths = new UpscalePaths(exe, _tempDir);

            Assert.True(paths.ExecutableExists);
            Assert.False(paths.IsUsable); // models/ nem existe

            UpscaleResult result = new UpscaleRunner(paths).Run(Path.Combine(_tempDir, "in.png"));

            Assert.Equal(UpscaleStatus.BinaryMissing, result.Status);
            Assert.Contains("AUSENTE", result.Diagnostics);
        }

        [Fact]
        public void Run_BinaryMissing_ReturnsBinaryMissing_WithoutSpawningAnything()
        {
            var paths = new UpscalePaths(Path.Combine(_tempDir, "does-not-exist.exe"), _tempDir);
            var runner = new UpscaleRunner(paths);

            UpscaleResult result = runner.Run(Path.Combine(_tempDir, "in.png"));

            Assert.Equal(UpscaleStatus.BinaryMissing, result.Status);
            Assert.Null(result.OutputPath);
        }
    }
}
