using System.Collections.Generic;
using System.Diagnostics;

namespace MantosExtract.Core.Upscale
{
    /// <summary>Resultado de um processo filho lançado por um <see cref="IChildProcessLauncher"/>.</summary>
    public sealed class ChildProcessResult
    {
        public bool TimedOut { get; set; }
        public int ExitCode { get; set; }
        public List<string> OutputLines { get; } = new List<string>();
        /// <summary>Como o processo foi lançado (vai pro diagnóstico).</summary>
        public string LaunchNote { get; set; } = "";
    }

    /// <summary>
    /// Ponto de injeção pra lançar o binário do upscale de um jeito especial quando o
    /// <see cref="Process"/> padrão não serve. Existe por causa de um detalhe do loader do Vulkan:
    /// ele IGNORA VK_DRIVER_FILES/VK_ICD_FILENAMES quando o processo tem integridade alta
    /// (<c>integrity_level >= SECURITY_MANDATORY_HIGH_RID</c> — loader_environment.c), e é por essa
    /// variável que o modo CPU aponta o lavapipe. Corel rodando elevado (ou VM com UAC desligado,
    /// onde todo processo nasce alto) herda isso pro filho e o modo CPU morre em
    /// "vkCreateInstance failed -9".
    /// </summary>
    public interface IChildProcessLauncher
    {
        /// <summary>Lança <paramref name="psi"/> (FileName, Arguments, WorkingDirectory,
        /// EnvironmentVariables) e espera até <paramref name="timeoutSeconds"/>. Devolve null quando
        /// este launcher não precisa agir — o chamador então usa o <see cref="Process"/> padrão.</summary>
        ChildProcessResult? TryRun(ProcessStartInfo psi, int timeoutSeconds);
    }
}
