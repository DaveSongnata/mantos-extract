using System;
using System.IO;

namespace MantosExtract.Core.Upscale
{
    /// <summary>
    /// Resolves the bundled <c>realesrgan-ncnn-vulkan.exe</c> binary — a pre-built, redistributable
    /// upstream tool (M5, CLAUDE.md), NOT something this repo compiles, so there is no "dev build
    /// under target/release" fallback the way SisCut.Engine.EnginePaths has for the Rust engine.
    /// Mirrors its precedence shape anyway (env override → installed path) for consistency.
    /// </summary>
    public sealed class UpscalePaths
    {
        public const string InstalledExeName = "realesrgan-ncnn-vulkan.exe";

        /// <summary>Nome do modelo geral (não-anime) a usar — o binário, sem <c>-n</c> explícito,
        /// cai no default upstream <c>realesr-animevideov3</c> (otimizado pra vídeo de anime), que
        /// é errado pra estampa/logo de roupa (Dave, 2026-09-11 — resolução de saída ruim no fundo
        /// extraído fez achar que o upscale nem rodava; na real o binário nunca esteve presente em
        /// nenhum build, e mesmo presente o modelo default seria o errado).</summary>
        public const string ModelName = "realesrgan-x4plus";

        public string ExecutablePath { get; }
        public string ModelsDir { get; }
        public string TempDir { get; }

        public UpscalePaths(string executablePath, string? tempDir = null)
        {
            ExecutablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
            string? exeDir = Path.GetDirectoryName(ExecutablePath);
            ModelsDir = string.IsNullOrEmpty(exeDir) ? "models" : Path.Combine(exeDir, "models");
            TempDir = tempDir ?? Path.Combine(Path.GetTempPath(), "MantosExtract", "upscale");
        }

        /// <summary>True when the resolved path actually exists — the packaging step (bundling
        /// the real binary + model files into the installer, plans/Phase_4.md) is NOT part of
        /// this codebase; a machine without it must degrade gracefully, never crash.</summary>
        public bool ExecutableExists => File.Exists(ExecutablePath);

        private static string? InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MantosExtract", "upscale");

        /// <summary>Resolves the binary: <c>MANTOSEXTRACT_UPSCALE_EXE</c> env → install path
        /// (<c>C:\Program Files\MantosExtract\upscale\realesrgan-ncnn-vulkan.exe</c>).</summary>
        public static UpscalePaths Resolve()
        {
            string? env = Environment.GetEnvironmentVariable("MANTOSEXTRACT_UPSCALE_EXE");
            if (!string.IsNullOrWhiteSpace(env)) return new UpscalePaths(env!);

            string installed = InstallDir is string dir ? Path.Combine(dir, InstalledExeName) : InstalledExeName;
            return new UpscalePaths(installed);
        }
    }
}
