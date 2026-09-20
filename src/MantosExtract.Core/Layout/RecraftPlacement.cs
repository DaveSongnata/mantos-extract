using System;

namespace MantosExtract.Core.Layout
{
    /// <summary>
    /// Encaixe do resultado da Recraft no Corel (Dave, 2026-09-20). O SVG não tem pixels: o Corel o
    /// importa num tamanho qualquer (proporção própria, às vezes levemente diferente da imagem que
    /// foi enviada). Escala UNIFORME pra caber dentro da caixa do original — nunca distorce.
    /// </summary>
    public static class RecraftPlacement
    {
        /// <summary>Tamanho final (mm) da peça nova: o maior que cabe na caixa do original mantendo
        /// a proporção da peça nova. Tamanho degenerado (zero/negativo) cai na caixa do original.</summary>
        public static (double WidthMm, double HeightMm) FitInside(
            double origWidthMm, double origHeightMm, double newWidth, double newHeight)
        {
            if (origWidthMm <= 0 || origHeightMm <= 0 || newWidth <= 0 || newHeight <= 0)
                return (origWidthMm, origHeightMm);

            double scale = Math.Min(origWidthMm / newWidth, origHeightMm / newHeight);
            return (newWidth * scale, newHeight * scale);
        }
    }
}