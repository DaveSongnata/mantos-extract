using System;
using System.Collections.Generic;

namespace MantosExtract.Core.Layout
{
    public readonly struct PlacedSlot
    {
        public double WidthMm { get; }
        public double HeightMm { get; }

        public PlacedSlot(double widthMm, double heightMm)
        {
            WidthMm = widthMm;
            HeightMm = heightMm;
        }
    }

    /// <summary>
    /// Pure left-to-right, wrapping row layout for imported elements — deliberately simple
    /// (this is a bootstrap/staging area on the canvas so the operator can grab each piece and
    /// place it themselves; SISBOLT owns real mold-fitting, out of scope per M6). The imported
    /// shape's real size is only known AFTER CorelDRAW imports it (Interop.CorelImporter), so
    /// this asks "where does the NEXT slot of size (w,h) go, given what's already placed" rather
    /// than pre-computing a full grid — each element is imported, measured, then moved here.
    /// </summary>
    public static class ElementLayout
    {
        public const double MarginMm = 10;
        public const double GapMm = 10;

        /// <summary>Returns the bottom-left corner (matching CorelDRAW's LeftX/BottomY
        /// convention, Y growing up) for the next element of size
        /// (<paramref name="nextWidthMm"/>, <paramref name="nextHeightMm"/>), given the page
        /// size and the elements already placed in this batch, in placement order.</summary>
        public static (double LeftMm, double BottomMm) NextSlot(
            double pageWidthMm, double pageHeightMm,
            double nextWidthMm, double nextHeightMm,
            IReadOnlyList<PlacedSlot> alreadyPlaced)
        {
            double cursorX = MarginMm;
            double rowTopY = pageHeightMm - MarginMm;
            double rowMaxHeight = 0;

            foreach (PlacedSlot slot in alreadyPlaced)
            {
                if (cursorX > MarginMm && cursorX + slot.WidthMm > pageWidthMm - MarginMm)
                {
                    cursorX = MarginMm;
                    rowTopY -= rowMaxHeight + GapMm;
                    rowMaxHeight = 0;
                }
                cursorX += slot.WidthMm + GapMm;
                rowMaxHeight = Math.Max(rowMaxHeight, slot.HeightMm);
            }

            if (cursorX > MarginMm && cursorX + nextWidthMm > pageWidthMm - MarginMm)
            {
                cursorX = MarginMm;
                rowTopY -= rowMaxHeight + GapMm;
            }

            double bottomMm = rowTopY - nextHeightMm;
            return (cursorX, bottomMm);
        }
    }
}
