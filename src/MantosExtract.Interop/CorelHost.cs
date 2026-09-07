using System;

namespace MantosExtract.Interop
{
    /// <summary>
    /// <see cref="ICorelHost"/> backed by the running CorelDRAW automation object, accessed by
    /// LATE BINDING (<c>dynamic</c>) so one assembly works against CorelDRAW 2024/2025/2026
    /// without a version-pinned PIA — same discipline as the sibling addins. The host
    /// application object is the one the addon framework injects into
    /// <c>MantosExtractDocker</c>'s constructor.
    /// </summary>
    public sealed class CorelHost : ICorelHost
    {
        private readonly dynamic _app; // Corel.Interop.VGCore.Application (late-bound)
        private dynamic? _lastImportedShape;

        public CorelHost(dynamic corelApplication)
        {
            _app = corelApplication;
        }

        public string DocumentName
        {
            get
            {
                try { return (string)_app.ActiveDocument.Name; }
                catch { return "(sem documento)"; }
            }
        }

        public int SelectionShapeCount
        {
            get
            {
                try
                {
                    var selection = _app.ActiveSelection;
                    if (selection == null) return 0;
                    return (int)selection.Shapes.Count;
                }
                catch { return 0; }
            }
        }

        public bool SelectionIsSingleBitmap
        {
            get
            {
                try
                {
                    var selection = _app.ActiveSelection;
                    if (selection == null) return false;
                    if ((int)selection.Shapes.Count != 1) return false;
                    dynamic shape = selection.Shapes[1]; // Corel COM collections are 1-based
                    return (int)shape.Type == CorelConstants.CdrBitmapShape;
                }
                catch { return false; }
            }
        }

        public void ExportSelectionToPng(string path, int dpi)
        {
            using var _ = new CorelDocumentState(_app);
            CorelExporter.ExportSelectionToPng(_app, _app.ActiveDocument, path, dpi);
        }

        public (double LeftMm, double BottomMm, double WidthMm, double HeightMm) ImportPng(string pngPath)
        {
            using var _ = new CorelDocumentState(_app);
            dynamic imported = CorelImporter.Import(_app, _app.ActiveDocument, pngPath);
            _lastImportedShape = imported;
            return ReadBoundsMm(imported);
        }

        public void MoveLastImportedShape(double leftMm, double bottomMm)
        {
            if (_lastImportedShape == null)
                throw new InvalidOperationException("Nenhuma peça importada ainda para posicionar.");

            using var _ = new CorelDocumentState(_app);
            try
            {
                _lastImportedShape.LeftX = leftMm;
                _lastImportedShape.BottomY = bottomMm;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Importado, mas não consegui posicionar a peça: " + ex.Message, ex);
            }
        }

        public void RenameLastImportedShape(string safeName)
        {
            if (_lastImportedShape == null) return;
            try { _lastImportedShape.Name = safeName; }
            catch { /* cosmetic only — a rename failure must never break the import */ }
        }

        public (double LeftMm, double BottomMm, double WidthMm, double HeightMm) ActivePageBoundsMm()
        {
            using var _ = new CorelDocumentState(_app);
            try
            {
                dynamic page = _app.ActiveDocument.ActivePage;
                double width = (double)page.SizeWidth;
                double height = (double)page.SizeHeight;
                // CorelDRAW's origin is the page's bottom-left in document coordinates once the
                // page itself is positioned at (0,0) in its own frame — SizeWidth/SizeHeight is
                // enough for the "place the first element near the page origin" heuristic; exact
                // page origin offset (if any) is a Fase 3 VM finding, not assumed here.
                return (0, 0, width, height);
            }
            catch { return (0, 0, 0, 0); }
        }

        private static (double, double, double, double) ReadBoundsMm(dynamic shape)
        {
            try { return ((double)shape.LeftX, (double)shape.BottomY, (double)shape.SizeWidth, (double)shape.SizeHeight); }
            catch { return (0, 0, 0, 0); }
        }
    }
}
