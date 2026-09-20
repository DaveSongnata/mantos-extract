using MantosExtract.Core.Layout;
using Xunit;

namespace MantosExtract.Core.Tests.Layout
{
    public class RecraftPlacementTests
    {
        [Fact]
        public void FitInside_SameAspect_MatchesTheOriginalBoxExactly()
        {
            var (w, h) = RecraftPlacement.FitInside(origWidthMm: 100, origHeightMm: 50, newWidth: 400, newHeight: 200);

            Assert.Equal(100, w, 6);
            Assert.Equal(50, h, 6);
        }

        [Fact]
        public void FitInside_WiderThanTheBox_ScalesByWidthAndKeepsTheAspect()
        {
            var (w, h) = RecraftPlacement.FitInside(100, 100, 300, 150);

            Assert.Equal(100, w, 6);
            Assert.Equal(50, h, 6);
        }

        [Fact]
        public void FitInside_TallerThanTheBox_ScalesByHeightAndKeepsTheAspect()
        {
            var (w, h) = RecraftPlacement.FitInside(100, 100, 150, 300);

            Assert.Equal(50, w, 6);
            Assert.Equal(100, h, 6);
        }

        [Fact]
        public void FitInside_NeverDistorts()
        {
            var (w, h) = RecraftPlacement.FitInside(80, 45, 1234, 987);

            Assert.Equal(1234.0 / 987.0, w / h, 6);
        }

        [Theory]
        [InlineData(0, 10, 5, 5)]
        [InlineData(10, 0, 5, 5)]
        [InlineData(10, 10, 0, 5)]
        [InlineData(10, 10, 5, 0)]
        public void FitInside_DegenerateSizes_FallBackToTheOriginalBox_NeverNaNOrInfinity(
            double ow, double oh, double nw, double nh)
        {
            var (w, h) = RecraftPlacement.FitInside(ow, oh, nw, nh);

            Assert.False(double.IsNaN(w) || double.IsInfinity(w));
            Assert.False(double.IsNaN(h) || double.IsInfinity(h));
            Assert.Equal(ow, w);
            Assert.Equal(oh, h);
        }
    }
}