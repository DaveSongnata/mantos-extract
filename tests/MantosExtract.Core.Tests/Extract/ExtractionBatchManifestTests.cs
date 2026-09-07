using System;
using System.Collections.Generic;
using MantosExtract.Core.Extract;
using Xunit;

namespace MantosExtract.Core.Tests.Extract
{
    public class ExtractionBatchManifestTests
    {
        [Fact]
        public void ToJson_FromJson_RoundTrip_PreservesAllFields()
        {
            var manifest = new ExtractionBatchManifest
            {
                BatchId = Guid.NewGuid(),
                CreatedAtUtc = new DateTime(2026, 9, 7, 18, 30, 0, DateTimeKind.Utc),
                Total = 3,
                Succeeded = 2,
                Failed = 1,
                Elements = new List<ExtractionBatchManifestElement>
                {
                    new ExtractionBatchManifestElement { Id = "el_1", Label = "Logo PR", FileName = "logo_pr.png", Ok = true },
                    new ExtractionBatchManifestElement { Id = "el_2", Label = "Texto Pulsar", FileName = "texto_pulsar.png", Ok = true },
                    new ExtractionBatchManifestElement { Id = "el_3", Label = "Texto You.C1000", FileName = null, Ok = false },
                },
            };

            string json = manifest.ToJson();
            ExtractionBatchManifest restored = ExtractionBatchManifest.FromJson(json);

            Assert.Equal(manifest.BatchId, restored.BatchId);
            Assert.Equal(manifest.CreatedAtUtc, restored.CreatedAtUtc);
            Assert.Equal(manifest.Total, restored.Total);
            Assert.Equal(manifest.Succeeded, restored.Succeeded);
            Assert.Equal(manifest.Failed, restored.Failed);
            Assert.Equal(manifest.Elements.Count, restored.Elements.Count);
            for (int i = 0; i < manifest.Elements.Count; i++)
            {
                Assert.Equal(manifest.Elements[i].Id, restored.Elements[i].Id);
                Assert.Equal(manifest.Elements[i].Label, restored.Elements[i].Label);
                Assert.Equal(manifest.Elements[i].FileName, restored.Elements[i].FileName);
                Assert.Equal(manifest.Elements[i].Ok, restored.Elements[i].Ok);
            }
        }

        [Fact]
        public void ToJson_FromJson_RoundTrip_EmptyElementsList()
        {
            var manifest = new ExtractionBatchManifest
            {
                BatchId = Guid.NewGuid(),
                CreatedAtUtc = DateTime.UtcNow,
                Total = 0,
                Succeeded = 0,
                Failed = 0,
                Elements = new List<ExtractionBatchManifestElement>(),
            };

            ExtractionBatchManifest restored = ExtractionBatchManifest.FromJson(manifest.ToJson());

            Assert.Empty(restored.Elements);
        }
    }
}
