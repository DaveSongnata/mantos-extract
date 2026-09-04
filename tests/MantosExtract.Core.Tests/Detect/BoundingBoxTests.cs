using MantosExtract.Core.Detect;
using Xunit;

namespace MantosExtract.Core.Tests.Detect
{
    public class BoundingBoxTests
    {
        [Fact]
        public void IsDegenerate_ZeroArea_ReturnsTrue()
        {
            Assert.True(new BoundingBox(100, 100, 100, 200).IsDegenerate); // xMax == xMin
            Assert.True(new BoundingBox(100, 200, 200, 200).IsDegenerate); // yMax == yMin
        }

        [Fact]
        public void IsDegenerate_Inverted_ReturnsTrue()
        {
            Assert.True(new BoundingBox(200, 100, 100, 200).IsDegenerate); // xMax < xMin
        }

        [Fact]
        public void IsDegenerate_NormalBox_ReturnsFalse()
        {
            Assert.False(new BoundingBox(100, 200, 300, 400).IsDegenerate);
        }
    }
}
