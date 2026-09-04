namespace MantosExtract.Interop
{
    /// <summary>
    /// VGCore enum values used by this add-in — every one grepped against
    /// ../../../optimus/docs/vgcore-tlb-dump.txt (VGCore 25.2 typelib dump) before use. NEVER
    /// guess a Corel constant: <c>cdrMillimeter=3</c> (4 is centimetres) is a 10x bug that has
    /// already shipped twice across the sibling repos.
    /// </summary>
    internal static class CorelConstants
    {
        // ENUM cdrShapeType (docs/vgcore-tlb-dump.txt) — verified in Optimus.Interop, reused
        // here unchanged (Optimus.CLAUDE.md "Verified constants in use").
        public const int CdrBitmapShape = 5;

        // ENUM cdrUnit
        public const int CdrMillimeter = 3;

        // ENUM cdrFilter (export/import filter ids)
        public const int CdrFilterPng = 802;

        // ENUM cdrExportRange
        public const int CdrExportRangeSelection = 2;

        // ENUM cdrAntiAliasingType
        public const int CdrNormalAntiAliasing = 1;

        // ENUM cdrImagePaletteType (StructPaletteOptions.PaletteType)
        public const int CdrPaletteOptimized = 3;

        // ENUM cdrImageType (StructExportOptions.ImageType)
        public const int CdrImageTypeRgb = 4;
    }
}
