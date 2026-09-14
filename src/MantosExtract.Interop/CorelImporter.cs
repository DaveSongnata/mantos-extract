using System;
using System.Collections.Generic;
using System.Reflection;
using MantosExtract.Core.Layout;

namespace MantosExtract.Interop
{
    /// <summary>
    /// Imports an extracted (and possibly upscaled) PNG into the active document's active
    /// layer — the output side of the pipeline (docs/mantos-extract-spec.md §7.3). Nothing in
    /// the sibling repos has ever written this direction before (they only export); the method
    /// itself is confirmed real against the typelib (<c>IVGLayer.Import</c>,
    /// ../../../optimus/docs/vgcore-tlb-dump.txt), but the exact post-import selection/position
    /// behaviour is UNCONFIRMED and must be validated on the VM (plans/Phase_3.md).
    ///
    /// Same discipline as CorelExporter: Type.InvokeMember only, never the `dynamic` binder.
    /// Import and positioning are deliberately TWO separate calls (see CorelHost): the imported
    /// shape's real size is only known AFTER import, and the layout decision itself
    /// (Core.Layout.ElementLayout) is pure/testable and must not be entangled with COM.
    /// </summary>
    public static class CorelImporter
    {
        /// <summary>Imports <paramref name="pngPath"/> into <paramref name="document"/>'s
        /// active layer and returns the imported shape (as <c>dynamic</c>) at whatever
        /// position/size Corel gave it.</summary>
        public static dynamic Import(dynamic application, dynamic document, string pngPath)
        {
            dynamic layer = document.ActiveLayer;
            List<int>? idsBefore = ReadStaticIds(layer);

            // Bug fix (Dave, 2026-09-07 — confirmed by real docker.log on the VM):
            // "COMException: Type mismatch (DISP_E_TYPEMISMATCH)" on every single Import call.
            // The previous code omitted both trailing optional args (Filter?, Options?),
            // assuming Corel's IDispatch would tolerate cArgs < full parameter count the way the
            // OLE Automation spec technically allows. It does not — this is the EXACT same
            // category of failure CorelExporter.cs already hit twice (ExportEx/ExportBitmap) and
            // fixed by always supplying every positional parameter explicitly. Import gets the
            // same treatment: never omit a trailing optional COM parameter via InvokeMember.
            dynamic options = application.CreateStructImportOptions();
            InvokeMember((object)layer, "Import",
                new object[] { pngPath, CorelConstants.CdrFilterPng, (object)options });

            // Primeiro pela diferença de StaticID (definitivo); a seleção só como último recurso,
            // e só se a leitura dos IDs de antes falhou — nunca devolve uma peça que já existia.
            dynamic? imported = idsBefore != null ? FindNewShape(layer, idsBefore) : null;
            string how = "diferença de StaticID";
            if (imported == null && idsBefore == null)
            {
                imported = ResolveImportedShape(document, layer);
                how = "seleção/último da camada (ids de antes ilegíveis)";
            }
            string layerName = "?";
            try { layerName = (string)layer.Name; } catch { }
            if (imported == null)
            {
                InteropLog.Write("importar " + System.IO.Path.GetFileName(pngPath) + ": NENHUM shape novo na camada \"" + layerName +
                                 "\" | antes=[" + (idsBefore != null ? string.Join(",", idsBefore) : "ilegível") +
                                 "] depois=[" + Join(ReadStaticIds(layer)) + "]");
                throw new InvalidOperationException(
                    "A imagem foi importada, mas não consegui localizar o objeto criado.");
            }
            InteropLog.Write("importar " + System.IO.Path.GetFileName(pngPath) + " na camada \"" + layerName + "\" via " + how +
                             ": " + InteropLog.ShapeInfo((object)imported) + " | shapes antes=" + (idsBefore?.Count.ToString() ?? "?"));
            return imported;
        }

        /// <summary>
        /// Corel automation conventionally leaves an Import's result as the active selection
        /// (docs/mantos-extract-spec.md §7.3 assumption, not independently proven anywhere in
        /// the sibling repos). Falls back to the layer's last shape by creation order if the
        /// active selection is empty or holds something else, so a Corel build that behaves
        /// differently still gets a shape back instead of a silent null.
        /// </summary>
        private static dynamic? ResolveImportedShape(dynamic document, dynamic layer)
        {
            try
            {
                dynamic selection = document.ActiveSelection;
                if (selection == null) throw new InvalidOperationException("no active selection");
                if ((int)selection.Shapes.Count >= 1)
                    return selection.Shapes[1]; // Corel COM collections are 1-based
            }
            catch { /* fall through to the layer-order fallback */ }

            try
            {
                dynamic shapes = layer.Shapes;
                int count = (int)shapes.Count;
                if (count >= 1) return shapes[count]; // most recently added, by convention
            }
            catch { /* nothing we can do */ }

            return null;
        }

        /// <summary>StaticIDs dos shapes de topo da camada, na ordem da coleção. Null se não deu
        /// pra ler (aí o chamador cai na seleção, como era antes).</summary>
        private static List<int>? ReadStaticIds(dynamic layer)
        {
            try
            {
                dynamic shapes = layer.Shapes;
                int count = (int)shapes.Count;
                var ids = new List<int>(count);
                for (int i = 1; i <= count; i++) ids.Add((int)shapes[i].StaticID);
                return ids;
            }
            catch (Exception ex)
            {
                InteropLog.Write("importar: StaticIDs da camada ilegíveis (" + InteropLog.Describe(ex) + ")");
                return null;
            }
        }

        private static string Join(List<int>? ids) => ids == null ? "ilegível" : string.Join(",", ids);

        private static dynamic? FindNewShape(dynamic layer, List<int> idsBefore)
        {
            List<int>? after = ReadStaticIds(layer);
            if (after == null) return null;
            int index = ImportedShapeResolver.IndexOfNewShape(new HashSet<int>(idsBefore), after, preferredIndex: 0);
            if (index < 0) return null;
            try { return layer.Shapes[index + 1]; } catch { return null; }
        }

        private static object? InvokeMember(object com, string method, object[] args) =>
            com.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, com, args);
    }
}
