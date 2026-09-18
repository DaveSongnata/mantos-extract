using MantosExtract.Core.Layout;
using Xunit;

namespace MantosExtract.Core.Tests.Layout
{
    public class RefinePlacementTests
    {
        [Fact]
        public void Beside_PutsTheRefinedPieceToTheRightOfTheOriginal_OnTheSameBaseline()
        {
            var (left, bottom) = RefinePlacement.Beside(leftMm: 20, bottomMm: 35, widthMm: 100);

            Assert.Equal(20 + 100 + ElementLayout.GapMm, left);
            Assert.Equal(35, bottom);
        }

        [Fact]
        public void Beside_KeepsTheGapBetweenNeighboursEvenForTinyPieces()
        {
            var (left, _) = RefinePlacement.Beside(0, 0, 1);

            Assert.Equal(1 + ElementLayout.GapMm, left);
        }

        [Fact]
        public void Beside_WorksWithNegativeCoordinates_TheDocumentOriginIsNotTheLeftEdge()
        {
            var (left, bottom) = RefinePlacement.Beside(-50, -20, 30);

            Assert.Equal(-50 + 30 + ElementLayout.GapMm, left);
            Assert.Equal(-20, bottom);
        }
    }
}