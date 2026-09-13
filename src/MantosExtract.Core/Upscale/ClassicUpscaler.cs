using System;
using System.Threading.Tasks;

namespace MantosExtract.Core.Upscale
{
    /// <summary>
    /// Último degrau da cadeia de upscale: ampliação 2× clássica (Lanczos-3 + leve nitidez) em
    /// código gerenciado puro. Existe pra que clicar em "Upscale" NUNCA termine em falha: não usa
    /// GPU, Vulkan, driver, DLL nativa nem processo externo — só CPU e memória, então nenhuma
    /// configuração de máquina (VM sem aceleração 3D, loader Vulkan antigo, antivírus bloqueando
    /// exe) consegue impedir. Qualidade abaixo da IA, acima de simplesmente esticar a imagem.
    ///
    /// Opera em BGRA 8 bits (layout de memória do GDI+ Format32bppArgb). Filtra em alpha
    /// PRÉ-MULTIPLICADO: sem isso, a cor "invisível" dos pixels transparentes vaza pra dentro da
    /// borda da estampa e cria um halo escuro/colorido em volta de todo elemento extraído.
    /// </summary>
    public static class ClassicUpscaler
    {
        private const int Factor = 2;
        private const int Lobes = 3;

        /// <summary>Amplia <paramref name="bgra"/> (w×h) 2× e devolve o novo buffer BGRA.</summary>
        public static byte[] Upscale2x(byte[] bgra, int width, int height, double sharpenAmount = 0.35)
        {
            if (bgra == null) throw new ArgumentNullException(nameof(bgra));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "dimensões inválidas");
            if (bgra.Length < (long)width * height * 4)
                throw new ArgumentException("buffer menor que width*height*4", nameof(bgra));

            int dw = width * Factor, dh = height * Factor;

            float[] pre = Premultiply(bgra, width, height);
            float[] horizontal = ResampleHorizontal(pre, width, height, dw);
            float[] full = ResampleVertical(horizontal, dw, height, dh);

            if (sharpenAmount > 0) full = Sharpen(full, dw, dh, (float)sharpenAmount);

            return Unpremultiply(full, dw, dh);
        }

        private static float[] Premultiply(byte[] src, int w, int h)
        {
            var dst = new float[w * h * 4];
            Parallel.For(0, h, y =>
            {
                int row = y * w * 4;
                for (int i = row; i < row + w * 4; i += 4)
                {
                    float a = src[i + 3] / 255f;
                    dst[i] = src[i] * a;
                    dst[i + 1] = src[i + 1] * a;
                    dst[i + 2] = src[i + 2] * a;
                    dst[i + 3] = src[i + 3];
                }
            });
            return dst;
        }

        private static byte[] Unpremultiply(float[] src, int w, int h)
        {
            var dst = new byte[w * h * 4];
            Parallel.For(0, h, y =>
            {
                int row = y * w * 4;
                for (int i = row; i < row + w * 4; i += 4)
                {
                    float a = Clamp(src[i + 3]);
                    dst[i + 3] = (byte)(a + 0.5f);
                    if (a < 0.5f) { dst[i] = dst[i + 1] = dst[i + 2] = 0; continue; }
                    float inv = 255f / a;
                    dst[i] = (byte)(Clamp(src[i] * inv) + 0.5f);
                    dst[i + 1] = (byte)(Clamp(src[i + 1] * inv) + 0.5f);
                    dst[i + 2] = (byte)(Clamp(src[i + 2] * inv) + 0.5f);
                }
            });
            return dst;
        }

        private static float Clamp(float v) => v < 0f ? 0f : (v > 255f ? 255f : v);

        private static double Lanczos(double x)
        {
            if (x == 0) return 1;
            if (x <= -Lobes || x >= Lobes) return 0;
            double px = Math.PI * x;
            return Lobes * Math.Sin(px) * Math.Sin(px / Lobes) / (px * px);
        }

        /// <summary>Pesos/índices de amostragem de UMA dimensão — iguais pra toda linha/coluna,
        /// então calculados uma vez só.</summary>
        private static (int[] start, float[,] weights, int taps) BuildKernel(int srcLen, int dstLen)
        {
            int taps = Lobes * 2;
            var start = new int[dstLen];
            var weights = new float[dstLen, taps];
            for (int d = 0; d < dstLen; d++)
            {
                double center = (d + 0.5) / Factor - 0.5;
                int first = (int)Math.Floor(center) - Lobes + 1;
                start[d] = first;
                double sum = 0;
                for (int t = 0; t < taps; t++)
                {
                    double wgt = Lanczos(center - (first + t));
                    weights[d, t] = (float)wgt;
                    sum += wgt;
                }
                for (int t = 0; t < taps; t++) weights[d, t] = (float)(weights[d, t] / sum);
            }
            return (start, weights, taps);
        }

        private static int ClampIndex(int i, int len) => i < 0 ? 0 : (i >= len ? len - 1 : i);

        private static float[] ResampleHorizontal(float[] src, int sw, int h, int dw)
        {
            var (start, weights, taps) = BuildKernel(sw, dw);
            var dst = new float[dw * h * 4];
            Parallel.For(0, h, y =>
            {
                int srow = y * sw * 4, drow = y * dw * 4;
                for (int dx = 0; dx < dw; dx++)
                {
                    float b = 0, g = 0, r = 0, a = 0;
                    for (int t = 0; t < taps; t++)
                    {
                        float wgt = weights[dx, t];
                        int s = srow + ClampIndex(start[dx] + t, sw) * 4;
                        b += src[s] * wgt; g += src[s + 1] * wgt; r += src[s + 2] * wgt; a += src[s + 3] * wgt;
                    }
                    int o = drow + dx * 4;
                    dst[o] = b; dst[o + 1] = g; dst[o + 2] = r; dst[o + 3] = a;
                }
            });
            return dst;
        }

        private static float[] ResampleVertical(float[] src, int w, int sh, int dh)
        {
            var (start, weights, taps) = BuildKernel(sh, dh);
            var dst = new float[w * dh * 4];
            Parallel.For(0, dh, dy =>
            {
                int drow = dy * w * 4;
                for (int x = 0; x < w; x++)
                {
                    float b = 0, g = 0, r = 0, a = 0;
                    for (int t = 0; t < taps; t++)
                    {
                        float wgt = weights[dy, t];
                        int s = (ClampIndex(start[dy] + t, sh) * w + x) * 4;
                        b += src[s] * wgt; g += src[s + 1] * wgt; r += src[s + 2] * wgt; a += src[s + 3] * wgt;
                    }
                    int o = drow + x * 4;
                    dst[o] = b; dst[o + 1] = g; dst[o + 2] = r; dst[o + 3] = a;
                }
            });
            return dst;
        }

        /// <summary>Unsharp mask 3×3 leve: devolve parte da definição que qualquer interpolação
        /// tira. Aplicado em premultiplicado também, pelo mesmo motivo do halo.</summary>
        private static float[] Sharpen(float[] src, int w, int h, float amount)
        {
            var dst = new float[src.Length];
            Parallel.For(0, h, y =>
            {
                int y0 = y > 0 ? y - 1 : 0, y1 = y < h - 1 ? y + 1 : h - 1;
                for (int x = 0; x < w; x++)
                {
                    int x0 = x > 0 ? x - 1 : 0, x1 = x < w - 1 ? x + 1 : w - 1;
                    int o = (y * w + x) * 4;
                    for (int c = 0; c < 4; c++)
                    {
                        float blur =
                            (src[(y0 * w + x0) * 4 + c] + 2 * src[(y0 * w + x) * 4 + c] + src[(y0 * w + x1) * 4 + c] +
                             2 * src[(y * w + x0) * 4 + c] + 4 * src[o + c] + 2 * src[(y * w + x1) * 4 + c] +
                             src[(y1 * w + x0) * 4 + c] + 2 * src[(y1 * w + x) * 4 + c] + src[(y1 * w + x1) * 4 + c]) / 16f;
                        dst[o + c] = src[o + c] + amount * (src[o + c] - blur);
                    }
                    // Cor premultiplicada nunca pode passar do próprio alpha (senão "estoura" a cor
                    // ao desfazer a premultiplicação).
                    float aa = Clamp(dst[o + 3]);
                    dst[o + 3] = aa;
                    for (int c = 0; c < 3; c++) dst[o + c] = dst[o + c] < 0 ? 0 : (dst[o + c] > aa ? aa : dst[o + c]);
                }
            });
            return dst;
        }
    }
}
