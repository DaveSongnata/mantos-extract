using System;
using System.Reflection;

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
        public static dynamic Import(dynamic document, string pngPath)
        {
            dynamic layer = document.ActiveLayer;

            // Import(FileName, Filter?, Options?) — both optional args omitted: Corel infers
            // the filter from the .png extension, and default StructImportOptions is what every
            // interactive File > Import does. If a future Corel build refuses the 1-arg call the
            // same way ExportEx once refused a missing struct, add a StructImportOptions here
            // (Application.CreateStructImportOptions(), confirmed to exist in the typelib) —
            // not done preemptively because it is unconfirmed which fields it needs.
            InvokeMember((object)layer, "Import", new object[] { pngPath });

            dynamic? imported = ResolveImportedShape(document, layer);
            if (imported == null)
                throw new InvalidOperationException(
                    "A imagem foi importada, mas não consegui localizar o objeto criado.");
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

        private static object? InvokeMember(object com, string method, object[] args) =>
            com.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, com, args);
    }
}
