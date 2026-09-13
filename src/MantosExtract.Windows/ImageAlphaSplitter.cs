using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MantosExtract.Windows
{
    /// <summary>
    /// Separa e recombina o canal alpha de um PNG. Existe por causa do modo CPU: o único modelo
    /// leve o bastante pra rodar sem GPU em tempo tolerável (realesr-animevideov3, 89s contra
    /// 640s do modelo principal) ZERA o alpha quando recebe RGBA — medido, a imagem inteira sai
    /// transparente. A saída é a clássica: mandar só o RGB pela rede, escalar o alpha por
    /// bicúbico à parte e juntar de novo. Todo elemento extraído é PNG transparente (M6), então
    /// sem isso o modo CPU só serviria pro fundo.
    ///
    /// Tudo por <c>LockBits</c>, nunca GetPixel/SetPixel: numa imagem 2508×2508 são 6,3 milhões
    /// de pixels, e a versão pixel-a-pixel leva minutos — mais que o upscale inteiro.
    /// </summary>
    public static class ImageAlphaSplitter
    {
        /// <summary>
        /// Escreve <paramref name="rgbPath"/> (opaco) e <paramref name="alphaPath"/> (cinza) a
        /// partir de <paramref name="inputPath"/>. Devolve false quando a imagem é totalmente
        /// opaca — aí não há nada a separar e o chamador manda o arquivo original direto.
        /// LANÇA em caso de erro (o pipeline de upscale loga a etapa com stack trace).
        /// </summary>
        public static bool Split(string inputPath, string rgbPath, string alphaPath)
        {
            try
            {
                using (var source = new Bitmap(inputPath))
                {
                    int w = source.Width, h = source.Height;
                    using (var rgba = source.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb))
                    using (var rgb = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    using (var alpha = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    {
                        BitmapData src = rgba.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        BitmapData dstRgb = rgb.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                        BitmapData dstAlpha = alpha.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                        bool anyTransparent = false;
                        try
                        {
                            int bytes = Math.Abs(src.Stride) * h;
                            var buffer = new byte[bytes];
                            var rgbOut = new byte[bytes];
                            var alphaOut = new byte[bytes];
                            Marshal.Copy(src.Scan0, buffer, 0, bytes);

                            // BGRA na memória. O alpha vai pros três canais de cor (cinza), que é
                            // o que o upscaler/bicúbico entende como imagem normal.
                            for (int i = 0; i < bytes; i += 4)
                            {
                                byte b = buffer[i], g = buffer[i + 1], r = buffer[i + 2], a = buffer[i + 3];
                                if (a != 255) anyTransparent = true;

                                rgbOut[i] = b; rgbOut[i + 1] = g; rgbOut[i + 2] = r; rgbOut[i + 3] = 255;
                                alphaOut[i] = a; alphaOut[i + 1] = a; alphaOut[i + 2] = a; alphaOut[i + 3] = 255;
                            }

                            Marshal.Copy(rgbOut, 0, dstRgb.Scan0, bytes);
                            Marshal.Copy(alphaOut, 0, dstAlpha.Scan0, bytes);
                        }
                        finally
                        {
                            rgba.UnlockBits(src);
                            rgb.UnlockBits(dstRgb);
                            alpha.UnlockBits(dstAlpha);
                        }

                        if (!anyTransparent) return false;

                        EnsureDir(rgbPath);
                        EnsureDir(alphaPath);
                        rgb.Save(rgbPath, ImageFormat.Png);
                        alpha.Save(alphaPath, ImageFormat.Png);
                        return true;
                    }
                }
            }
            catch
            {
                TryDelete(rgbPath);
                TryDelete(alphaPath);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>
        /// Junta o RGB já ampliado com o alpha original (escalado por bicúbico até o tamanho do
        /// RGB) em <paramref name="outputPath"/>. Bicúbico é suficiente aqui: o alpha de uma
        /// estampa é uma máscara de recorte, não textura — o que importa é a borda continuar no
        /// lugar, e é a rede que cuida do detalhe visível, que está no RGB.
        /// </summary>
        public static void Combine(string upscaledRgbPath, string originalAlphaPath, string outputPath)
        {
            try
            {
                using (var rgb = new Bitmap(upscaledRgbPath))
                using (var alphaSource = new Bitmap(originalAlphaPath))
                {
                    int w = rgb.Width, h = rgb.Height;
                    using (var alphaScaled = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(alphaScaled))
                        {
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(alphaSource, new Rectangle(0, 0, w, h));
                        }

                        using (var rgb32 = rgb.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb))
                        using (var result = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                        {
                            BitmapData rgbData = rgb32.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                            BitmapData alphaData = alphaScaled.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                            BitmapData outData = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                            try
                            {
                                int bytes = Math.Abs(rgbData.Stride) * h;
                                var rgbBuf = new byte[bytes];
                                var alphaBuf = new byte[bytes];
                                var outBuf = new byte[bytes];
                                Marshal.Copy(rgbData.Scan0, rgbBuf, 0, bytes);
                                Marshal.Copy(alphaData.Scan0, alphaBuf, 0, bytes);

                                for (int i = 0; i < bytes; i += 4)
                                {
                                    outBuf[i] = rgbBuf[i];
                                    outBuf[i + 1] = rgbBuf[i + 1];
                                    outBuf[i + 2] = rgbBuf[i + 2];
                                    outBuf[i + 3] = alphaBuf[i + 2]; // canal R do cinza = alpha
                                }

                                Marshal.Copy(outBuf, 0, outData.Scan0, bytes);
                            }
                            finally
                            {
                                rgb32.UnlockBits(rgbData);
                                alphaScaled.UnlockBits(alphaData);
                                result.UnlockBits(outData);
                            }

                            EnsureDir(outputPath);
                            result.Save(outputPath, ImageFormat.Png);
                        }
                    }
                }
            }
            catch
            {
                TryDelete(outputPath);
                throw;
            }
        }

        private static void EnsureDir(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
    }
}
