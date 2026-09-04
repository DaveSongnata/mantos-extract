using System;
using System.Reflection;

namespace MantosExtract.Interop
{
    /// <summary>
    /// Exports the current CorelDRAW selection to a PNG file — the input side of the pipeline
    /// (the exported bitmap is what gets sent to /mantos-extract/detect and /extract). Mirrors
    /// SisCut.Interop.CorelExporter's discipline exactly, trimmed to PNG-only (no PDF/TIFF/JPEG,
    /// no column re-selection: the operator already selected the photo manually, seção 7.1 of
    /// docs/mantos-extract-spec.md — we export whatever ActiveSelection already is).
    ///
    /// ALL calls go through Type.InvokeMember (classic OLE reflection), NEVER the C# `dynamic`
    /// binder: the DLR COM binder reads Corel's typeinfo and refuses the argument coercion up
    /// front — both `ExportEx(StructExportOptions)` and `ExportBitmap(primitives)` died on the
    /// VM with "Could not convert argument 0" (lesson paid twice already, SisCut and Optimus).
    /// </summary>
    public static class CorelExporter
    {
        /// <summary>Exports the active selection to <paramref name="path"/> as PNG at
        /// <paramref name="dpi"/>. Throws on failure — the caller (bridge) turns that into a
        /// pt-BR message, same as every other COM entry point in this add-in.</summary>
        public static void ExportSelectionToPng(dynamic application, dynamic document, string path, int dpi)
        {
            try { ExportViaStructOptions(application, document, path, dpi); }
            catch (Exception primaryEx)
            {
                try { ExportViaBitmapFallback(document, path, dpi); }
                catch (Exception fallbackEx)
                {
                    throw new InvalidOperationException(
                        "Falha ao exportar a seleção para PNG: " + Root(primaryEx).Message +
                        " (fallback também falhou: " + Root(fallbackEx).Message + ")", primaryEx);
                }
            }
        }

        private static void ExportViaStructOptions(dynamic application, dynamic document, string path, int dpi)
        {
            dynamic opts = application.CreateStructExportOptions();
            opts.ImageType = CorelConstants.CdrImageTypeRgb;
            opts.ResolutionX = dpi;
            opts.ResolutionY = dpi;
            opts.AntiAliasingType = CorelConstants.CdrNormalAntiAliasing;
            try { opts.Transparent = false; } catch { /* not every host version exposes it */ }
            opts.Overwrite = true;

            dynamic pal = application.CreateStructPaletteOptions();
            pal.PaletteType = CorelConstants.CdrPaletteOptimized;

            Finish(InvokeMember((object)document, "ExportEx", new object[]
            {
                path, CorelConstants.CdrFilterPng, CorelConstants.CdrExportRangeSelection, (object)opts, (object)pal,
            }));
        }

        /// <summary>14-arg ExportBitmap, primitives only — the fallback the sibling repos
        /// already proved necessary when ExportEx's struct binding fails on a given Corel
        /// build.</summary>
        private static void ExportViaBitmapFallback(dynamic document, string path, int dpi)
        {
            Finish(InvokeMember((object)document, "ExportBitmap", new object[]
            {
                path, CorelConstants.CdrFilterPng, CorelConstants.CdrExportRangeSelection,
                CorelConstants.CdrImageTypeRgb,
                0, 0, 0, 0, // Left/Top/SizeX/SizeY = 0 -> whole selection bbox
                dpi, dpi,
                false, // Transparent
                false, // OnlySelected (range already limits to selection)
                CorelConstants.CdrNormalAntiAliasing,
                CorelConstants.CdrPaletteOptimized,
            }));
        }

        private static void Finish(object? exportFilter)
        {
            if (exportFilter != null) InvokeMember(exportFilter, "Finish", Array.Empty<object>());
        }

        private static object? InvokeMember(object com, string method, object[] args) =>
            com.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, com, args);

        private static Exception Root(Exception e) =>
            e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
    }
}
