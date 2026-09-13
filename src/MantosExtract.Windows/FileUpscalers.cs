using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using MantosExtract.Core.Upscale;

namespace MantosExtract.Windows
{
    /// <summary>
    /// Os dois degraus finais da cadeia de upscale, operando em arquivo. Nenhum depende de GPU,
    /// Vulkan, driver ou processo externo — é isso que garante que o botão "Upscale" sempre
    /// termina com uma imagem 2×, mesmo numa máquina onde toda a parte de IA falhou.
    /// Ambos LANÇAM em caso de erro; quem chama loga a etapa com stack trace.
    /// </summary>
    public static class FileUpscalers
    {
        /// <summary>Degrau 3: Lanczos-3 + nitidez (ClassicUpscaler, código gerenciado puro).</summary>
        public static void Classic2x(string inputPath, string outputPath)
        {
            using (var source = new Bitmap(inputPath))
            {
                int w = source.Width, h = source.Height;
                byte[] src;
                using (var rgba = source.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb))
                {
                    BitmapData data = rgba.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        // Stride pode ter padding; copia linha a linha pro buffer compacto w*4.
                        src = new byte[w * h * 4];
                        int stride = Math.Abs(data.Stride);
                        var row = new byte[stride];
                        for (int y = 0; y < h; y++)
                        {
                            Marshal.Copy(IntPtr.Add(data.Scan0, y * stride), row, 0, stride);
                            Buffer.BlockCopy(row, 0, src, y * w * 4, w * 4);
                        }
                    }
                    finally { rgba.UnlockBits(data); }
                }

                byte[] dst = ClassicUpscaler.Upscale2x(src, w, h);
                int dw = w * 2, dh = h * 2;

                using (var result = new Bitmap(dw, dh, PixelFormat.Format32bppArgb))
                {
                    result.SetResolution(source.HorizontalResolution, source.VerticalResolution);
                    BitmapData outData = result.LockBits(new Rectangle(0, 0, dw, dh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        int stride = Math.Abs(outData.Stride);
                        for (int y = 0; y < dh; y++)
                            Marshal.Copy(dst, y * dw * 4, IntPtr.Add(outData.Scan0, y * stride), dw * 4);
                    }
                    finally { result.UnlockBits(outData); }
                    Save(result, outputPath);
                }
            }
        }

        /// <summary>
        /// Degrau 4 (rede de segurança final): bicúbico do próprio GDI+, que vem com o Windows.
        /// Só roda se o Lanczos gerenciado falhou (ex.: memória insuficiente numa imagem enorme —
        /// este caminho usa bem menos). Premultiplicado pelo mesmo motivo do halo.
        /// </summary>
        public static void Gdi2x(string inputPath, string outputPath)
        {
            using (var source = new Bitmap(inputPath))
            using (var result = new Bitmap(source.Width * 2, source.Height * 2, PixelFormat.Format32bppPArgb))
            {
                result.SetResolution(source.HorizontalResolution, source.VerticalResolution);
                using (var g = Graphics.FromImage(result))
                using (var attrs = new ImageAttributes())
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    attrs.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(source, new Rectangle(0, 0, result.Width, result.Height),
                        0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attrs);
                }
                Save(result, outputPath);
            }
        }

        private static void Save(Bitmap bmp, string outputPath)
        {
            string? dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            try { bmp.Save(outputPath, ImageFormat.Png); }
            catch
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                throw;
            }
        }
    }
}
