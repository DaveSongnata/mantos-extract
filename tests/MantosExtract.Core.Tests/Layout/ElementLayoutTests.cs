using System.Collections.Generic;
using MantosExtract.Core.Layout;
using Xunit;

namespace MantosExtract.Core.Tests.Layout
{
    public class ElementLayoutTests
    {
        [Fact]
        public void FirstElement_GoesNearTopLeftOfThePage()
        {
            var (left, bottom) = ElementLayout.NextSlot(
                pageWidthMm: 1000, pageHeightMm: 800,
                nextWidthMm: 100, nextHeightMm: 100,
                alreadyPlaced: System.Array.Empty<PlacedSlot>());

            Assert.Equal(ElementLayout.MarginMm, left);
            Assert.Equal(800 - ElementLayout.MarginMm - 100, bottom);
        }

        [Fact]
        public void SecondElement_GoesToTheRightOfTheFirst_SameRow()
        {
            var placed = new[] { new PlacedSlot(100, 100) };

            var (left, bottom) = ElementLayout.NextSlot(
                pageWidthMm: 1000, pageHeightMm: 800,
                nextWidthMm: 100, nextHeightMm: 100,
                alreadyPlaced: placed);

            Assert.Equal(ElementLayout.MarginMm + 100 + ElementLayout.GapMm, left);
            Assert.Equal(800 - ElementLayout.MarginMm - 100, bottom); // same row -> same Y as the first
        }

        [Fact]
        public void ElementThatWouldOverflowPageWidth_WrapsToNewRow()
        {
            var placed = new List<PlacedSlot>();
            double pageWidth = 250;
            // Fill the row until the next 100mm-wide element would overflow.
            placed.Add(new PlacedSlot(100, 60));
            placed.Add(new PlacedSlot(100, 60));

            var (left, bottom) = ElementLayout.NextSlot(
                pageWidthMm: pageWidth, pageHeightMm: 800,
                nextWidthMm: 100, nextHeightMm: 60,
                alreadyPlaced: placed);

            // cursor after two 100mm items + gaps = 10+100+10+100+10 = 230; +100 = 330 > 250-10 -> wraps
            Assert.Equal(ElementLayout.MarginMm, left);
            Assert.True(bottom < 800 - ElementLayout.MarginMm - 60, "should have dropped to a second row");
        }

        [Fact]
        public void RowHeight_UsesTheTallestElementInTheRow()
        {
            // pageWidth=150: item1 (w=50) then item2 (w=50) both fit in row 1 (cursor reaches
            // 130 <= 150-10), but a third w=50 item would reach 180 > 140 and must wrap.
            var placed = new[] { new PlacedSlot(50, 30), new PlacedSlot(50, 90) };

            var (_, bottomOfThird) = ElementLayout.NextSlot(
                pageWidthMm: 150, pageHeightMm: 800,
                nextWidthMm: 50, nextHeightMm: 40,
                alreadyPlaced: placed);

            // Row advances by the TALLEST item in the row (90), not the shortest (30).
            double expectedRowTop = 800 - ElementLayout.MarginMm - 90 - ElementLayout.GapMm;
            Assert.Equal(expectedRowTop - 40, bottomOfThird);
        }

        [Fact]
        public void SingleElementWiderThanThePage_StillPlacedAtMargin_NeverInfiniteLoop()
        {
            var (left, bottom) = ElementLayout.NextSlot(
                pageWidthMm: 100, pageHeightMm: 800,
                nextWidthMm: 500, nextHeightMm: 100,
                alreadyPlaced: System.Array.Empty<PlacedSlot>());

            Assert.Equal(ElementLayout.MarginMm, left);
            Assert.Equal(800 - ElementLayout.MarginMm - 100, bottom);
        }
    }
}
