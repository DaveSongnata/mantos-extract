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
                "-i C:\\temp\\in.png -o C:\\temp\\out.png -s 4 -m C:\\temp\\models -n realesrgan-x4plus-anime",
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
                "-m \"C:\\Program Files\\models\" -n realesrgan-x4plus-anime",
                args);
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
