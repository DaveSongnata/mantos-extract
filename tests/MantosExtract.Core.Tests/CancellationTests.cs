using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MantosExtract.Core.Detect;
using MantosExtract.Core.Extract;
using MantosExtract.Core.Upscale;
using Xunit;

namespace MantosExtract.Core.Tests
{
    /// <summary>
    /// "Cancelar enquanto está processando" (pedido do cliente). O que é testável sem Corel:
    /// que o cancelamento chega de verdade no HTTP (detecção/extração/fundo) e no processo do
    /// upscale — e que cancelar NUNCA vira "erro de rede" nem "falha".
    /// </summary>
    public class CancellationTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "MantosExtractCancel_" + Guid.NewGuid().ToString("N"));
        public CancellationTests() => Directory.CreateDirectory(_tempDir);
        public void Dispose() { try { Directory.Delete(_tempDir, true); } catch { } }

        /// <summary>Servidor que nunca responde — só o cancelamento tira a chamada daqui.</summary>
        private sealed class HangingHandler : HttpMessageHandler
        {
            public bool SawCancellation { get; private set; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                try { await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { SawCancellation = true; throw; }
                throw new InvalidOperationException("inalcançável");
            }
        }

        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47 };

        [Fact]
        public async Task Detect_Cancelled_AbortsTheHttpCall_AndIsNotANetworkError()
        {
            var handler = new HangingHandler();
            var client = new DetectionClient(handler, new Uri("https://mantosfc.test"));
            using var cts = new CancellationTokenSource(300);

            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.DetectAsync("sessao", "sk-test", Png, "image/png", cts.Token));

            Assert.True(handler.SawCancellation);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "cancelamento demorou " + sw.Elapsed);
        }

        [Fact]
        public async Task Extract_Cancelled_AbortsTheHttpCall_AndIsNotANetworkError()
        {
            var handler = new HangingHandler();
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));
            using var cts = new CancellationTokenSource(300);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.ExtractAsync("sessao", "sk-test", "medium", Png, "image/png",
                    new BoundingBox(10, 10, 200, 200), "logo", cts.Token));

            Assert.True(handler.SawCancellation);
        }

        [Fact]
        public async Task ExtractBackground_Cancelled_AbortsTheHttpCall()
        {
            var handler = new HangingHandler();
            var client = new ExtractionClient(handler, new Uri("https://mantosfc.test"));
            using var cts = new CancellationTokenSource(300);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.ExtractBackgroundAsync("sessao", "sk-test", "medium", Png, "image/png", cts.Token));

            Assert.True(handler.SawCancellation);
        }

        [Fact]
        public void UpscaleProcess_CancelledMidway_IsKilledRightAway()
        {
            // ping de 30s faz o papel do Real-ESRGAN demorando (modo CPU leva minutos).
            string never = Path.Combine(_tempDir, "nunca.png");
            using var cts = new CancellationTokenSource(500);

            var sw = Stopwatch.StartNew();
            UpscaleStatus status = UpscaleRunner.RunProcess(
                Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n 30 127.0.0.1", never,
                timeoutSeconds: 120, out string output, ct: cts.Token);

            Assert.Equal(UpscaleStatus.Cancelled, status);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "processo não morreu na hora: " + sw.Elapsed);
            Assert.Contains("CANCELADO", output);
        }

        [Fact]
        public void UpscaleProcess_AlreadyCancelled_NeverStarts()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            UpscaleStatus status = UpscaleRunner.RunProcess(
                Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n 30 127.0.0.1",
                Path.Combine(_tempDir, "nunca.png"), 120, out _, ct: cts.Token);

            Assert.Equal(UpscaleStatus.Cancelled, status);
        }

        [Fact]
        public void UpscaleProcess_NotCancelled_StillTimesOutAsBefore()
        {
            // Regressão: trocar o WaitForExit único pelo laço de fatias não pode quebrar o timeout.
            var sw = Stopwatch.StartNew();
            UpscaleStatus status = UpscaleRunner.RunProcess(
                Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n 30 127.0.0.1",
                Path.Combine(_tempDir, "nunca.png"), 1, out _);

            Assert.Equal(UpscaleStatus.Timeout, status);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8));
        }

        [Fact]
        public void UpscaleRunner_PassesCancellationThrough_ToTheLauncher()
        {
            // O modo CPU pode passar pelo MediumIntegrityLauncher: o token tem que chegar lá.
            var launcher = new RecordingLauncher();
            string exe = Path.Combine(_tempDir, UpscalePaths.InstalledExeName);
            File.WriteAllText(exe, "x");
            var paths = new UpscalePaths(exe, _tempDir);
            Directory.CreateDirectory(paths.ModelsDir);
            Directory.CreateDirectory(paths.CpuVulkanDir);
            File.WriteAllText(paths.ModelParamPath, "p"); File.WriteAllText(paths.ModelBinPath, "b");
            File.WriteAllText(paths.CpuIcdPath, "{}");
            File.WriteAllText(Path.Combine(paths.CpuVulkanDir, "vulkan_lvp.dll"), "d");
            string stem = Path.Combine(paths.ModelsDir, UpscalePaths.CpuModelName + "-x" + UpscalePaths.CpuNativeScale);
            File.WriteAllText(stem + ".param", "p"); File.WriteAllText(stem + ".bin", "b");

            using var cts = new CancellationTokenSource();
            UpscaleResult r = new UpscaleRunner(paths, launcher).Run(Path.Combine(_tempDir, "in.png"), 30, UpscaleDevice.Cpu, cts.Token);

            Assert.True(launcher.Called);
            Assert.Equal(cts.Token, launcher.Token);
            Assert.Equal(UpscaleStatus.Cancelled, r.Status);
        }

        private sealed class RecordingLauncher : IChildProcessLauncher
        {
            public bool Called { get; private set; }
            public CancellationToken Token { get; private set; }
            public ChildProcessResult? TryRun(ProcessStartInfo psi, int timeoutSeconds, CancellationToken ct)
            {
                Called = true;
                Token = ct;
                return new ChildProcessResult { Cancelled = true, LaunchNote = "teste" };
            }
        }
    }
}
