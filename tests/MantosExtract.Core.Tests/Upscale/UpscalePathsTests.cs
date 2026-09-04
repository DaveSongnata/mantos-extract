using System;
using System.IO;
using MantosExtract.Core.Upscale;
using Xunit;

namespace MantosExtract.Core.Tests.Upscale
{
    public class UpscalePathsTests
    {
        [Fact]
        public void ExecutableExists_FalseForMissingFile()
        {
            var paths = new UpscalePaths(Path.Combine(Path.GetTempPath(), "definitely-not-here.exe"));

            Assert.False(paths.ExecutableExists);
        }

        [Fact]
        public void ExecutableExists_TrueForRealFile()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                var paths = new UpscalePaths(tempFile);
                Assert.True(paths.ExecutableExists);
            }
            finally { File.Delete(tempFile); }
        }

        [Fact]
        public void Resolve_EnvOverride_TakesPrecedence()
        {
            string original = Environment.GetEnvironmentVariable("MANTOSEXTRACT_UPSCALE_EXE") ?? "";
            try
            {
                Environment.SetEnvironmentVariable("MANTOSEXTRACT_UPSCALE_EXE", @"D:\custom\realesrgan.exe");

                UpscalePaths resolved = UpscalePaths.Resolve();

                Assert.Equal(@"D:\custom\realesrgan.exe", resolved.ExecutablePath);
            }
            finally { Environment.SetEnvironmentVariable("MANTOSEXTRACT_UPSCALE_EXE", string.IsNullOrEmpty(original) ? null : original); }
        }
    }
}
