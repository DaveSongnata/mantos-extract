using MantosExtract.Core.Extract;
using Xunit;

namespace MantosExtract.Core.Tests.Extract
{
    public class ExtractionPresetsTests
    {
        [Fact]
        public void All_CoversEveryQualityTheServerAccepts_InAscendingOrder()
        {
            Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max" }, ExtractionPresets.All);
        }

        [Fact]
        public void Default_IsMedium_SameAsBeforeTheMigration()
        {
            Assert.Equal("medium", ExtractionPresets.Default);
        }

        [Theory]
        [InlineData("low")]
        [InlineData("medium")]
        [InlineData("high")]
        [InlineData("xhigh")]
        [InlineData("max")]
        public void Normalize_KeepsValidPresets(string preset)
        {
            Assert.Equal(preset, ExtractionPresets.Normalize(preset));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("ultra")]
        [InlineData("auto")]
        [InlineData("MAX")]
        [InlineData("gpt-image-2.5-sunburst")]
        public void Normalize_FallsBackToDefault_NeverSendsSomethingTheServerWouldReject(string? value)
        {
            Assert.Equal(ExtractionPresets.Default, ExtractionPresets.Normalize(value));
        }

        [Theory]
        [InlineData("low", true)]
        [InlineData("max", true)]
        [InlineData("auto", false)]
        [InlineData(null, false)]
        public void IsValid_MatchesTheAllowlist(string? value, bool expected)
        {
            Assert.Equal(expected, ExtractionPresets.IsValid(value));
        }
    }
}
