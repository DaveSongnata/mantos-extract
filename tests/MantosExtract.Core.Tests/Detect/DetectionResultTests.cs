using MantosExtract.Core.Api;
using MantosExtract.Core.Detect;
using Xunit;

namespace MantosExtract.Core.Tests.Detect
{
    public class DetectionResultTests
    {
        [Fact]
        public void Parse_TypicalResponse_ReadsAllElements()
        {
            string json = @"{
                ""elements"": [
                    { ""id"": ""el_1"", ""label"": ""logo"", ""bbox"": { ""x_min"": 120, ""y_min"": 300, ""x_max"": 400, ""y_max"": 550 } },
                    { ""id"": ""el_2"", ""label"": ""texto MARCOS"", ""bbox"": { ""x_min"": 10, ""y_min"": 20, ""x_max"": 900, ""y_max"": 80 } }
                ]
            }";

            DetectionResult result = DetectionResult.Parse(json);

            Assert.Equal(2, result.Elements.Count);
            Assert.Equal("logo", result.Elements[0].Label);
            Assert.Equal(120, result.Elements[0].Box.XMin);
            Assert.Equal("texto MARCOS", result.Elements[1].Label);
        }

        [Fact]
        public void Parse_NoElementsFound_ReturnsEmptyList_NotAnError()
        {
            DetectionResult result = DetectionResult.Parse(@"{""elements"":[]}");

            Assert.Empty(result.Elements);
        }

        [Fact]
        public void Parse_DegenerateBoxAmongValidOnes_SkipsOnlyTheBadOne()
        {
            string json = @"{
                ""elements"": [
                    { ""id"": ""el_1"", ""label"": ""ok"", ""bbox"": { ""x_min"": 10, ""y_min"": 10, ""x_max"": 100, ""y_max"": 100 } },
                    { ""id"": ""el_2"", ""label"": ""degenerada"", ""bbox"": { ""x_min"": 50, ""y_min"": 50, ""x_max"": 50, ""y_max"": 50 } }
                ]
            }";

            DetectionResult result = DetectionResult.Parse(json);

            Assert.Single(result.Elements);
            Assert.Equal("ok", result.Elements[0].Label);
        }

        [Fact]
        public void Parse_MissingElementsField_ReturnsEmptyList()
        {
            DetectionResult result = DetectionResult.Parse(@"{}");

            Assert.Empty(result.Elements);
        }

        [Fact]
        public void Parse_MalformedJson_ThrowsMalformedResponse()
        {
            MantosExtractApiException ex = Assert.Throws<MantosExtractApiException>(() => DetectionResult.Parse("not json"));
            Assert.Equal("E_MALFORMED_RESPONSE", ex.Code);
        }
    }
}
