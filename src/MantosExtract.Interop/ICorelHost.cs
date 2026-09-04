namespace MantosExtract.Interop
{
    /// <summary>
    /// The seam between the bridge and CorelDRAW's COM automation. Everything that touches the
    /// running CorelDRAW instance goes through this interface — same shape as SisCut.AddIn's
    /// ICorelHost.
    /// </summary>
    public interface ICorelHost
    {
        /// <summary>Active document's display name, or a placeholder when none is open.</summary>
        string DocumentName { get; }

        /// <summary>Number of shapes in the active selection (0 if nothing/closed doc).</summary>
        int SelectionShapeCount { get; }

        /// <summary>True only when the selection is EXACTLY one bitmap shape — the only
        /// selection state "Detectar" (Fase 2) is enabled for.</summary>
        bool SelectionIsSingleBitmap { get; }

        /// <summary>Exports the current selection to <paramref name="path"/> as PNG. Throws a
        /// message already safe to log/relay on failure.</summary>
        void ExportSelectionToPng(string path, int dpi);

        /// <summary>Imports <paramref name="pngPath"/> into the active layer at whatever
        /// position/size Corel gives it natively. Returns (LeftX, BottomY, SizeWidth,
        /// SizeHeight) so the caller can compute a real layout slot (Core.Layout.ElementLayout,
        /// pure/testable) BEFORE moving it — the size isn't known until after import.</summary>
        (double LeftMm, double BottomMm, double WidthMm, double HeightMm) ImportPng(string pngPath);

        /// <summary>Moves the most recently imported shape's bottom-left corner to
        /// (<paramref name="leftMm"/>, <paramref name="bottomMm"/>) in document millimetres.</summary>
        void MoveLastImportedShape(double leftMm, double bottomMm);

        /// <summary>Renames the most recently imported shape — sanitized by the caller before
        /// it ever reaches here (docs/mantos-extract-spec.md §6, "José &amp; Cia" case).</summary>
        void RenameLastImportedShape(string safeName);

        /// <summary>Active page bounding box in millimetres, used to place the first imported
        /// element sensibly when there is no prior element to lay out next to.</summary>
        (double LeftMm, double BottomMm, double WidthMm, double HeightMm) ActivePageBoundsMm();
    }
}
