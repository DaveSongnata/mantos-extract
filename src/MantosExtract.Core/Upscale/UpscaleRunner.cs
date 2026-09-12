using System;
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
        /// <summary>Exited non-zero, or exited 0 without producing the expected output file.</summary>
        Failed,
    }

    public sealed class UpscaleResult
    {
        public UpscaleStatus Status { get; }
        public string? OutputPath { get; }

        private UpscaleResult(UpscaleStatus status, string? outputPath)
        {
            Status = status;
            OutputPath = outputPath;
        }

        public static UpscaleResult Ok(string outputPath) => new UpscaleResult(UpscaleStatus.Success, outputPath);
        public static UpscaleResult Of(UpscaleStatus status) => new UpscaleResult(status, null);
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
        public UpscaleResult Run(string inputPngPath, int timeoutSeconds = 90)
        {
            if (!_paths.ExecutableExists) return UpscaleResult.Of(UpscaleStatus.BinaryMissing);

            Directory.CreateDirectory(_paths.TempDir);
            string outputPath = Path.Combine(_paths.TempDir,
                Path.GetFileNameWithoutExtension(inputPngPath) + "_native" + UpscalePaths.NativeScale + "x.png");
            if (File.Exists(outputPath)) File.Delete(outputPath);

            string arguments = BuildArguments(
                inputPngPath, outputPath, _paths.ModelsDir, UpscalePaths.ModelName, UpscalePaths.NativeScale);
            UpscaleStatus status = RunProcess(_paths.ExecutablePath, arguments, outputPath, timeoutSeconds);
            return status == UpscaleStatus.Success ? UpscaleResult.Ok(outputPath) : UpscaleResult.Of(status);
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
        public static string BuildArguments(string inputPath, string outputPath, string modelsDir, string modelName, int scale) =>
            $"-i {Quote(inputPath)} -o {Quote(outputPath)} -s {scale} -m {Quote(modelsDir)} -n {Quote(modelName)}";

        /// <summary>
        /// Generic external-process runner: start, wait up to <paramref name="timeoutSeconds"/>,
        /// kill on timeout, and check BOTH the exit code and that <paramref name="expectedOutputPath"/>
        /// actually exists — a 0 exit with no output file is still a failure, never a silent
        /// pass-through of the un-upscaled input.
        /// </summary>
        internal static UpscaleStatus RunProcess(string exePath, string arguments, string expectedOutputPath, int timeoutSeconds)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };

            using Process? proc = Process.Start(psi);
            if (proc == null) return UpscaleStatus.Failed;

            if (!proc.WaitForExit(timeoutSeconds * 1000))
            {
                try { proc.Kill(); } catch { /* already gone */ }
                try { proc.WaitForExit(); } catch { /* best effort */ }
                return UpscaleStatus.Timeout;
            }

            if (proc.ExitCode != 0) return UpscaleStatus.Failed;
            return File.Exists(expectedOutputPath) ? UpscaleStatus.Success : UpscaleStatus.Failed;
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
