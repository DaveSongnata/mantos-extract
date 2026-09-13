using MantosExtract.Core.Upscale;
using Xunit;

namespace MantosExtract.Core.Tests.Upscale
{
    public class ClassicUpscalerTests
    {
        private static byte[] Fill(int w, int h, byte b, byte g, byte r, byte a)
        {
            var buf = new byte[w * h * 4];
            for (int i = 0; i < buf.Length; i += 4) { buf[i] = b; buf[i + 1] = g; buf[i + 2] = r; buf[i + 3] = a; }
            return buf;
        }

        [Fact]
        public void DoublesBothDimensions()
        {
            byte[] outBuf = ClassicUpscaler.Upscale2x(Fill(7, 5, 10, 20, 30, 255), 7, 5);
            Assert.Equal(14 * 10 * 4, outBuf.Length);
        }

        [Fact]
        public void SolidOpaqueColor_StaysTheSameColor()
        {
            byte[] outBuf = ClassicUpscaler.Upscale2x(Fill(16, 16, 40, 120, 250, 255), 16, 16);
            for (int i = 0; i < outBuf.Length; i += 4)
            {
                Assert.InRange(outBuf[i], 39, 41);
                Assert.InRange(outBuf[i + 1], 119, 121);
                Assert.InRange(outBuf[i + 2], 249, 251);
                Assert.Equal(255, outBuf[i + 3]);
            }
        }

        [Fact]
        public void FullyTransparent_StaysFullyTransparent()
        {
            byte[] outBuf = ClassicUpscaler.Upscale2x(Fill(10, 10, 0, 0, 255, 0), 10, 10);
            for (int i = 3; i < outBuf.Length; i += 4) Assert.Equal(0, outBuf[i]);
        }

        [Fact]
        public void TransparentNeighbourColor_DoesNotBleedIntoTheOpaquePart()
        {
            // Esquerda: vermelho TRANSPARENTE (cor "invisível", típico de PNG extraído).
            // Direita: azul opaco. Sem premultiplicação, a borda azul ganharia um halo vermelho.
            const int w = 20, h = 4;
            var buf = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    if (x < w / 2) { buf[i + 2] = 255; buf[i + 3] = 0; }       // vermelho, alpha 0
                    else { buf[i] = 255; buf[i + 3] = 255; }                   // azul opaco
                }

            byte[] outBuf = ClassicUpscaler.Upscale2x(buf, w, h);
            int dw = w * 2;
            for (int y = 0; y < h * 2; y++)
                for (int x = 0; x < dw; x++)
                {
                    int i = (y * dw + x) * 4;
                    if (outBuf[i + 3] > 32) Assert.True(outBuf[i + 2] < 24, $"halo vermelho em ({x},{y}): R={outBuf[i + 2]} A={outBuf[i + 3]}");
                }
        }

        [Fact]
        public void HardEdge_KeepsBothSidesAtTheirOriginalValues()
        {
            const int w = 20, h = 2;
            var buf = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    byte v = x < w / 2 ? (byte)0 : (byte)255;
                    buf[i] = buf[i + 1] = buf[i + 2] = v; buf[i + 3] = 255;
                }

            byte[] outBuf = ClassicUpscaler.Upscale2x(buf, w, h);
            Assert.InRange(outBuf[0], 0, 2);                        // bem à esquerda: preto
            Assert.InRange(outBuf[(w * 2 - 1) * 4], 253, 255);      // bem à direita: branco
        }

        [Fact]
        public void TinyImages_DoNotThrow()
        {
            Assert.Equal(4, ClassicUpscaler.Upscale2x(Fill(1, 1, 1, 2, 3, 255), 1, 1).Length / 4);
            Assert.Equal(8, ClassicUpscaler.Upscale2x(Fill(2, 1, 1, 2, 3, 128), 2, 1).Length / 4);
        }
    }
}
