using System;
using System.Collections.Generic;

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

        // Shapes desta sessão, por id de elemento — só pro upscale opcional (ver
        // TrackLastImportedShape). Guardar a referência COM direto (em vez de procurar por nome
        // na hora) é o que garante que o upscale troque EXATAMENTE a peça que aquela linha da
        // tela de resultado representa, mesmo que o operador tenha renomeado ou movido ela.
        private readonly Dictionary<string, object> _trackedShapes = new Dictionary<string, object>();

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

        public void SaveSelectedBitmapForUpscale(string pngPath, string trackKey)
        {
            using var _ = new CorelDocumentState(_app);

            dynamic selection = _app.ActiveSelection
                ?? throw new InvalidOperationException("Nenhuma seleção ativa no CorelDRAW.");
            if ((int)selection.Shapes.Count != 1)
                throw new InvalidOperationException("Selecione exatamente UMA imagem para o upscale.");
            dynamic shape = selection.Shapes[1]; // coleções COM do Corel são 1-based
            if ((int)shape.Type != CorelConstants.CdrBitmapShape)
                throw new InvalidOperationException("A seleção não é uma imagem (bitmap).");

            // IVGBitmap.SaveAs(FileName, Filter, Compression?) — confirmado na typelib. Todos os
            // parâmetros explícitos via InvokeMember (omitir opcional em COM já quebrou Import com
            // DISP_E_TYPEMISMATCH). Devolve um ExportFilter que precisa de Finish().
            object bitmap = shape.Bitmap;
            object? filter = bitmap.GetType().InvokeMember("SaveAs", System.Reflection.BindingFlags.InvokeMethod,
                null, bitmap, new object[] { pngPath, CorelConstants.CdrFilterPng, CorelConstants.CdrCompressionNone });
            if (filter != null)
                filter.GetType().InvokeMember("Finish", System.Reflection.BindingFlags.InvokeMethod, null, filter, Array.Empty<object>());

            if (!System.IO.File.Exists(pngPath))
                throw new InvalidOperationException("O CorelDRAW não gravou a imagem selecionada (" + pngPath + ").");

            _trackedShapes[trackKey] = (object)shape;
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

        public void TrackLastImportedShape(string key)
        {
            if (_lastImportedShape == null || string.IsNullOrEmpty(key)) return;
            // `!`: o null-check acima já garante, mas a análise de fluxo não atravessa `dynamic?`.
            _trackedShapes[key] = (object)_lastImportedShape!;
        }

        public bool ReplaceTrackedShape(string key, string pngPath)
        {
            if (!_trackedShapes.TryGetValue(key, out object? tracked) || tracked == null) return false;

            using var _ = new CorelDocumentState(_app);

            // Lê a geometria do antigo ANTES de importar qualquer coisa: se ele foi apagado à mão
            // pelo operador, isso lança e a gente sai SEM ter jogado uma segunda cópia na página.
            double leftMm, bottomMm, widthMm, heightMm;
            try
            {
                dynamic old = tracked;
                leftMm = (double)old.LeftX;
                bottomMm = (double)old.BottomY;
                widthMm = (double)old.SizeWidth;
                heightMm = (double)old.SizeHeight;
            }
            catch
            {
                _trackedShapes.Remove(key);
                return false;
            }

            // Nome do shape antigo: a versão em alta herda o mesmo nome (no upscale avulso da
            // seleção, o nome é do operador e não pode virar "Bitmap").
            string? oldName = null;
            try { oldName = (string)((dynamic)tracked).Name; } catch { /* nome é cosmético */ }

            dynamic imported = CorelImporter.Import(_app, _app.ActiveDocument, pngPath);
            _lastImportedShape = imported;
            if (!string.IsNullOrEmpty(oldName)) { try { imported.Name = oldName; } catch { } }

            // Tamanho primeiro, posição depois: mexer no tamanho reposiciona a âncora no Corel, e
            // o que importa é o resultado final ficar EXATAMENTE na caixa do antigo — upscale
            // muda a densidade de pixels, nunca o tamanho físico da peça na página.
            try
            {
                imported.SizeWidth = widthMm;
                imported.SizeHeight = heightMm;
                imported.LeftX = leftMm;
                imported.BottomY = bottomMm;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "A versão em alta resolução entrou, mas não consegui encaixá-la no lugar da anterior: " + ex.Message, ex);
            }

            try { ((dynamic)tracked).Delete(); }
            catch { /* a nova já está no lugar certo; uma sobra invisível não justifica falhar */ }

            _trackedShapes[key] = (object)imported;
            return true;
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
