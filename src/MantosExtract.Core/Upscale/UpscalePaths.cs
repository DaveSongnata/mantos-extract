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

        /// <summary>
        /// Modelo usado, escolhido por teste A/B real (Dave, 2026-09-11) e NÃO pelo nome:
        /// "anime" aqui quer dizer "treinado em arte ilustrada — borda dura, cor chapada", que é
        /// exatamente o que uma estampa/logo/escudo de camisa é. No crop 1:1 lado a lado contra
        /// o <c>realesrgan-x4plus</c> (treinado em foto real), este devolveu bordas visivelmente
        /// mais limpas, além de ser 2,7× mais rápido (7,8s vs 20,9s numa RTX 3050, imagem
        /// 1254×1254) e ter modelo 3,7× menor (8,9MB vs 33MB — instalador menor).
        /// Preserva o canal alpha corretamente (testado: alphaMin=0/alphaMax=255 num PNG com
        /// transparência real), diferente do <c>realesr-animevideov3</c>, que é o mais rápido de
        /// todos (2,6s) mas ZERA o alpha — inutilizável aqui, onde todo elemento extraído é PNG
        /// transparente (M6).
        /// </summary>
        public const string ModelName = "realesrgan-x4plus-anime";

        /// <summary>
        /// Escala NATIVA da rede deste modelo. Tem que ser passada no <c>-s</c> exatamente como é:
        /// pedir <c>-s 2</c> de um modelo nativo 4× não produz um 2× — o binário monta os tiles em
        /// posições calculadas pra 2× enquanto a rede devolve tiles 4×, e o resultado sai num
        /// MOSAICO de blocos desencontrados (bug real, achado visualmente em 2026-09-11 comparando
        /// <c>-s 2</c> contra <c>-s 4</c> na mesma foto). O 2× final que o produto entrega (M8) é
        /// obtido reduzindo este 4× pela metade depois — ver ImageDownscaler/ApplyUpscale.
        /// </summary>
        public const int NativeScale = 4;

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

        /// <summary>
        /// Onde o instalador REALMENTE põe o binário: numa subpasta <c>upscale\</c> ao lado das
        /// DLLs do addon, dentro do Addons do Corel
        /// (<c>...\CorelDRAW Graphics Suite X\Programs64\Addons\MantosExtract\upscale\</c>).
        /// Esta é a primeira opção porque o caminho fixo em Program Files abaixo NUNCA existiu
        /// numa instalação real — o instalador jamais escreveu lá (bug achado 2026-09-11: mesmo
        /// com o binário empacotado, Resolve() procurava num lugar onde ele nunca esteve).
        /// </summary>
        private static string? BesideAddonAssembly()
        {
            try
            {
                string? dir = Path.GetDirectoryName(typeof(UpscalePaths).Assembly.Location);
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir!, "upscale", InstalledExeName);
            }
            catch { return null; }
        }

        /// <summary>Resolves the binary: <c>MANTOSEXTRACT_UPSCALE_EXE</c> env → pasta do addon
        /// (<c>&lt;Addons&gt;\MantosExtract\upscale\</c>, onde o instalador põe) → caminho fixo
        /// em Program Files (só como último recurso, pra uma instalação manual).</summary>
        public static UpscalePaths Resolve()
        {
            string? env = Environment.GetEnvironmentVariable("MANTOSEXTRACT_UPSCALE_EXE");
            if (!string.IsNullOrWhiteSpace(env)) return new UpscalePaths(env!);

            string? beside = BesideAddonAssembly();
            if (beside != null && File.Exists(beside)) return new UpscalePaths(beside);

            string installed = InstallDir is string dir ? Path.Combine(dir, InstalledExeName) : InstalledExeName;
            return new UpscalePaths(installed);
        }
    }
}
