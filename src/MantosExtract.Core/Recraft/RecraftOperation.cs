namespace MantosExtract.Core.Recraft
{
    /// <summary>
    /// As duas capacidades da Recraft que o addin oferece (Dave, 2026-09-20). São chamadas
    /// INDEPENDENTES sobre a mesma entrada — nunca uma encadeada na outra (o experimento de
    /// 2026-09-19 mostrou que removeBackground sobre SVG piora). Quem quer um vetor sem fundo
    /// remove o fundo primeiro e depois vetoriza a peça nova, por conta própria.
    /// </summary>
    public enum RecraftOperation
    {
        RemoveBackground,
        Vectorize,
    }

    public static class RecraftOperations
    {
        public static string EndpointPath(RecraftOperation operation) =>
            operation == RecraftOperation.Vectorize
                ? "/api/v1/mantos-extract/vectorize"
                : "/api/v1/mantos-extract/remove-background";

        /// <summary>O resultado é SVG (vetor) e não PNG — muda o filtro de import no Corel.</summary>
        public static bool ProducesVector(RecraftOperation operation) => operation == RecraftOperation.Vectorize;

        /// <summary>Sufixo do nome da peça nova no Corel (o original mantém o nome dele).</summary>
        public static string NameSuffix(RecraftOperation operation) =>
            operation == RecraftOperation.Vectorize ? " (vetor)" : " (sem fundo)";

        /// <summary>Nome estável pro docker.log e pros arquivos gravados em disco.</summary>
        public static string LogName(RecraftOperation operation) =>
            operation == RecraftOperation.Vectorize ? "vetorizar" : "remover-fundo";
    }
}