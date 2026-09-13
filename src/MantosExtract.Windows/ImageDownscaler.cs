using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace MantosExtract.Windows
{
    /// <summary>
    /// Halves a PNG, preserving transparency. Exists because Real-ESRGAN only tiles correctly at
    /// its model's NATIVE scale (4x — see UpscalePaths.NativeScale): the product ships 2x (M8),
    /// so the pipeline upscales 4x and halves here. That is not a detour, it is strictly better
    /// than asking for 2x directly — supersampling from 4x averages away the network's own
    /// artefacts instead of shipping them at full strength.
    /// </summary>
    public static class ImageDownscaler
    {
        /// <summary>Writes a half-size copy of <paramref name="inputPath"/> to
        /// <paramref name="outputPath"/>. THROWS on failure (no partial output left behind): the
        /// upscale pipeline logs every step with its stack trace, which a swallowed bool can't give.</summary>
        public static void Halve(string inputPath, string outputPath)
        {
            try
            {
                using (var source = new Bitmap(inputPath))
                {
                    int width = Math.Max(1, source.Width / 2);
                    int height = Math.Max(1, source.Height / 2);

                    // 32bppArgb + a transparent clear: the default surface would be opaque black
                    // and every transparent pixel of an extracted element (M6) would come out
                    // filled in.
                    using (var target = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                    {
                        target.SetResolution(source.HorizontalResolution, source.VerticalResolution);
                        using (var g = Graphics.FromImage(target))
                        {
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.CompositingQuality = CompositingQuality.HighQuality;
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.SmoothingMode = SmoothingMode.HighQuality;
                            g.Clear(Color.Transparent);

                            // Wrap-mode TileFlipXY stops the half-pixel edge halo bicubic
                            // otherwise leaves on the outer border.
                            using (var attrs = new ImageAttributes())
                            {
                                attrs.SetWrapMode(WrapMode.TileFlipXY);
                                g.DrawImage(source, new Rectangle(0, 0, width, height),
                                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attrs);
                            }
                        }

                        string? dir = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        target.Save(outputPath, ImageFormat.Png);
                    }
                }
            }
            catch
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
                throw;
            }
        }
    }
}
