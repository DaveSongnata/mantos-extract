namespace MantosExtract.Core.Layout
{
    /// <summary>
    /// Onde a peça refinada entra no canvas (Dave, 2026-09-18): AO LADO do original, nunca por
    /// cima, pra o original continuar intacto e recuperável. Mesma linha de base, à direita, com
    /// o mesmo respiro (<see cref="ElementLayout.GapMm"/>) que separa as peças de um lote. O
    /// tamanho da peça nova é o do original — só a densidade de pixels muda, nunca o tamanho
    /// físico na página.
    /// </summary>
    public static class RefinePlacement
    {
        /// <summary>Canto inferior esquerdo (convenção LeftX/BottomY do CorelDRAW, Y pra cima) da
        /// peça nova, dado o retângulo do original em mm.</summary>
        public static (double LeftMm, double BottomMm) Beside(double leftMm, double bottomMm, double widthMm)
        {
            return (leftMm + widthMm + ElementLayout.GapMm, bottomMm);
        }
    }
}