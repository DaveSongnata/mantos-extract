using System;
using System.IO;
using MantosExtract.Core.Extract;
using Xunit;

namespace MantosExtract.Core.Tests.Extract
{
    public class ExtractionBatchNamingTests
    {
        [Fact]
        public void FolderName_KnownInputs_ProducesExpectedString()
        {
            var batchId = Guid.Parse("a1b2c3d4-0000-0000-0000-000000000000");
            var timestamp = new DateTime(2026, 9, 7, 15, 47, 22);

            string result = ExtractionBatchNaming.FolderName(batchId, timestamp, 6);

            Assert.Equal("20260907-154722_a1b2c3d4_6el", result);
        }

        [Fact]
        public void FolderName_SingleDigitCount_NoPadding()
        {
            var batchId = Guid.Parse("ffffffff-0000-0000-0000-000000000000");
            var timestamp = new DateTime(2026, 1, 1, 0, 0, 0);

            string result = ExtractionBatchNaming.FolderName(batchId, timestamp, 1);

            Assert.Equal("20260101-000000_ffffffff_1el", result);
        }

        [Fact]
        public void FolderName_DoubleDigitCount_NoSeparatorConfusion()
        {
            var batchId = Guid.Parse("12345678-0000-0000-0000-000000000000");
            var timestamp = new DateTime(2026, 12, 31, 23, 59, 59);

            string result = ExtractionBatchNaming.FolderName(batchId, timestamp, 42);

            Assert.Equal("20261231-235959_12345678_42el", result);
        }

        [Fact]
        public void FolderName_NeverContainsInvalidFileNameChars()
        {
            string result = ExtractionBatchNaming.FolderName(Guid.NewGuid(), DateTime.Now, 6);

            foreach (char c in Path.GetInvalidFileNameChars())
                Assert.DoesNotContain(c, result);
        }
    }
}
